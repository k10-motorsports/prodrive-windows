using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RaceCorProDrive.Support;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Release-info poller for prodrive-windows. Migrated out of the
    /// SimHub plugin so the host (the user's primary surface) owns the
    /// update flow. The plugin keeps a static version display only.
    ///
    /// Singleton with INotifyPropertyChanged so the Settings/System
    /// "Update" card can bind reactively to status changes.
    ///
    /// Talks to the prodrive-server proxy at
    ///   {WebAppBase}/api/v1/host/latest-release
    /// rather than GitHub's API directly. The repo
    /// (k10-motorsports/prodrive-windows) is private, so an unauthenticated
    /// request to api.github.com/.../releases/latest returns 404 and the
    /// auto-updater silently never finds an update. The proxy holds the
    /// GitHub token server-side and rewrites asset URLs to point back at
    /// {WebAppBase}/api/download/latest, which resolves a fresh signed
    /// URL on every hit so the host never needs a token of its own.
    ///
    /// Asset name pattern: RaceCorProDrive-Setup-{version}-{arch}.exe
    /// (Inno Setup output — see installer/RaceCorProDrive.iss).
    /// Two assets per release: x64 + arm64. We pick the one matching
    /// the running process arch.
    /// </summary>
    public sealed class UpdateService : INotifyPropertyChanged
    {
        public static UpdateService Shared { get; } = new();

        // ── Config ──────────────────────────────────────────────
        // Web-app proxy that forwards the GitHub API call with a
        // server-side token. The path is the v1 host endpoint —
        // dev/staging override the host part via RACECOR_WEB_BASE.
        private static readonly string ReleasesApi =
            (Environment.GetEnvironmentVariable("RACECOR_WEB_BASE")?.TrimEnd('/')
             ?? "https://prodrive.racecor.io")
            + "/api/v1/host/latest-release";
        private const string UserAgent = "RaceCorProDrive-Host";
        // AppSettings key for the auto-update toggle. Default true —
        // racing rigs are usually unattended; getting the latest fix
        // overnight is friendlier than asking each time.
        public const string SettingKey_AutoUpdate = "racecor.host.autoUpdate";
        // Tracks the last version we asked the OS to install. If the
        // running assembly still reports an older version on next
        // launch (e.g. CI didn't stamp the version → assembly stays at
        // the dev placeholder), we treat that as a failed install
        // attempt and refuse to retry the same target version. Without
        // this guard a broken stamp puts the app in a download/install
        // loop on every poll. The retry cooldown unblocks legitimate
        // re-attempts after 24h.
        private const string SettingKey_LastInstallTarget = "racecor.host.lastInstallTarget";
        private const string SettingKey_LastInstallAt     = "racecor.host.lastInstallAt";
        private static readonly TimeSpan ReinstallCooldown = TimeSpan.FromHours(24);
        // Re-poll the GitHub API every hour while the app is running.
        // GitHub allows 60 anonymous requests/hour per IP; once an hour
        // is well under any reasonable cap.
        private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly HttpClient _http;
        private Timer? _pollTimer;
        private bool _started;

        public string CurrentVersion { get; }
        public string? LatestVersion { get; private set; }
        public string? DownloadUrl { get; private set; }
        public string? ReleaseNotes { get; private set; }
        public bool UpdateAvailable { get; private set; }
        public bool IsChecking { get; private set; }
        public bool IsDownloading { get; private set; }
        public int DownloadPercent { get; private set; }
        public string? ErrorMessage { get; private set; }
        /// <summary>UTC time of the last successful CheckForUpdateAsync.</summary>
        public DateTime? LastCheckedAt { get; private set; }

        public bool AutoUpdateEnabled
        {
            get => AppSettings.GetBool(SettingKey_AutoUpdate, defaultValue: true);
            set { AppSettings.SetBool(SettingKey_AutoUpdate, value); Raise(nameof(AutoUpdateEnabled)); }
        }

        private UpdateService()
        {
            CurrentVersion = ReadAssemblyVersion();
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        /// <summary>
        /// Kick off an immediate update check + start the hourly poll.
        /// Idempotent. Called from App.OnLaunched alongside the iRacing
        /// detector and Moza service.
        /// </summary>
        public void Start()
        {
            if (_started) return;
            _started = true;

            // First check on app start, fire-and-forget.
            _ = CheckAndMaybeInstallAsync();

            // Hourly thereafter. Timer callbacks fire on a worker thread —
            // PropertyChanged listeners on the UI thread should marshal
            // back via DispatcherQueue (the Settings page does this in
            // its OnPropertyChanged handler).
            _pollTimer = new Timer(
                _ => _ = CheckAndMaybeInstallAsync(),
                state: null,
                dueTime: PollInterval,
                period: PollInterval);
        }

        /// <summary>
        /// Public manual-trigger for the Settings card's "Check now"
        /// button. Same code path as the timer-driven check.
        /// </summary>
        public Task CheckNowAsync() => CheckAndMaybeInstallAsync(forceInstall: false);

        /// <summary>
        /// Public manual-trigger for the "Download &amp; install" button.
        /// Skips the check (assumes UpdateAvailable is true) and goes
        /// straight to the download.
        /// </summary>
        public Task InstallAsync() => DownloadAndInstallAsync();

        // ── Internal flow ───────────────────────────────────────

        private async Task CheckAndMaybeInstallAsync(bool forceInstall = false)
        {
            await CheckForUpdateAsync().ConfigureAwait(false);
            if (!UpdateAvailable) return;
            if (!AutoUpdateEnabled && !forceInstall) return;

            // Defense-in-depth against the 0.18.1-style install loop:
            // if we already tried to install this same LatestVersion in
            // the last 24h and the current assembly still doesn't match
            // it, the previous install didn't stick (broken version
            // stamp, UAC declined, antivirus quarantine, etc.). Retrying
            // immediately just spawns the installer over and over,
            // interrupting whatever the user is doing. forceInstall
            // bypasses the cooldown so the manual "Install" button
            // always works.
            if (!forceInstall && JustTriedThisVersion(LatestVersion))
            {
                Debug.WriteLine($"[Update] suppressing auto-install of {LatestVersion}: same target attempted within {ReinstallCooldown.TotalHours:0}h");
                return;
            }

            // Don't interrupt a session. If iRacing (or anything that
            // pushes telemetry to the plugin) is live, defer until the
            // next hourly poll. forceInstall still wins so the user can
            // explicitly opt in mid-session.
            if (!forceInstall && IRacingDetector.Shared.IsRunning)
            {
                Debug.WriteLine("[Update] deferring install: iRacing is running");
                return;
            }

            await DownloadAndInstallAsync().ConfigureAwait(false);
        }

        private async Task CheckForUpdateAsync()
        {
            if (IsChecking) return;
            IsChecking = true;
            ErrorMessage = null;
            Raise(nameof(IsChecking));
            Raise(nameof(ErrorMessage));

            try
            {
                using var resp = await _http.GetAsync(ReleasesApi).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream).ConfigureAwait(false);
                if (release == null)
                {
                    ErrorMessage = "Empty release payload.";
                    UpdateAvailable = false;
                }
                else
                {
                    LatestVersion = (release.TagName ?? "").TrimStart('v');
                    ReleaseNotes = release.Body ?? "";

                    // Prefer the asset matching this process's arch
                    // (x64 / arm64). Falls through to any Setup asset
                    // if the arch-specific one isn't present.
                    var arch = RuntimeInformation.ProcessArchitecture switch
                    {
                        Architecture.Arm64 => "arm64",
                        _ => "x64",
                    };
                    DownloadUrl = PickAsset(release.Assets, arch) ?? PickAsset(release.Assets, null);
                    UpdateAvailable = !string.IsNullOrEmpty(DownloadUrl)
                        && IsNewerVersion(LatestVersion, CurrentVersion);

                    // Clear the "just tried" marker once the running
                    // assembly catches up. Without this, the cooldown
                    // would lock out a future legitimate update to a
                    // newer version that happens to also collide with
                    // a previously-failed target.
                    var lastTarget = AppSettings.Get(SettingKey_LastInstallTarget);
                    if (!string.IsNullOrEmpty(lastTarget) && !IsNewerVersion(lastTarget, CurrentVersion))
                    {
                        AppSettings.Remove(SettingKey_LastInstallTarget);
                        AppSettings.Remove(SettingKey_LastInstallAt);
                    }
                }
                LastCheckedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] check failed: {ex.Message}");
                ErrorMessage = $"Update check failed: {ex.Message}";
                UpdateAvailable = false;
            }
            finally
            {
                IsChecking = false;
                Raise(nameof(IsChecking));
                Raise(nameof(LatestVersion));
                Raise(nameof(DownloadUrl));
                Raise(nameof(ReleaseNotes));
                Raise(nameof(UpdateAvailable));
                Raise(nameof(LastCheckedAt));
                Raise(nameof(ErrorMessage));
            }
        }

        private async Task DownloadAndInstallAsync()
        {
            if (IsDownloading || string.IsNullOrEmpty(DownloadUrl) || string.IsNullOrEmpty(LatestVersion)) return;
            IsDownloading = true;
            DownloadPercent = 0;
            ErrorMessage = null;
            Raise(nameof(IsDownloading));
            Raise(nameof(DownloadPercent));
            Raise(nameof(ErrorMessage));

            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(),
                    $"RaceCorProDrive-Setup-{LatestVersion}.exe");

                using (var resp = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    var total = resp.Content.Headers.ContentLength ?? -1;
                    using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        bufferSize: 64 * 1024, useAsync: true);
                    var buffer = new byte[64 * 1024];
                    long received = 0;
                    int n;
                    while ((n = await src.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                        received += n;
                        if (total > 0)
                        {
                            var pct = (int)Math.Round(received * 100.0 / total);
                            if (pct != DownloadPercent)
                            {
                                DownloadPercent = pct;
                                Raise(nameof(DownloadPercent));
                            }
                        }
                    }
                }

                // Mark this version as "we tried to install it" BEFORE
                // launching the installer. If the installer succeeds,
                // the relaunched app's CheckForUpdateAsync clears the
                // marker (the version on disk now matches). If it
                // fails / is cancelled / silently no-ops, the marker
                // stays and JustTriedThisVersion() suppresses the next
                // hour's retry — breaking the loop.
                AppSettings.Set(SettingKey_LastInstallTarget, LatestVersion);
                AppSettings.Set(SettingKey_LastInstallAt, DateTime.UtcNow.ToString("O"));

                // Inno Setup is a normal Windows .exe — ShellExecute
                // gives the OS the option of UAC-prompting. The installer
                // can self-handle "running app needs to close" via its
                // [Setup] AppMutex / CloseApplications directives.
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] download failed: {ex.Message}");
                ErrorMessage = $"Download failed: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
                Raise(nameof(IsDownloading));
                Raise(nameof(ErrorMessage));
            }
        }

        // ── Helpers ─────────────────────────────────────────────

        private static string ReadAssemblyVersion()
        {
            // InformationalVersion respects <Version> from the csproj
            // (which the CI release pipeline stamps from the git tag).
            // AssemblyVersion is the legacy 4-part System.Version which
            // includes a ".0" build suffix — we want the human string.
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // Strip "+commitsha" suffix sometimes appended by the SDK.
                var plus = info.IndexOf('+');
                return plus > 0 ? info.Substring(0, plus) : info;
            }
            var ver = asm.GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.0.0";
        }

        private static string? PickAsset(List<GitHubAsset>? assets, string? archHint)
        {
            if (assets == null) return null;
            foreach (var a in assets)
            {
                var name = a.Name ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) continue;
                if (archHint != null && !name.Contains(archHint, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(a.BrowserDownloadUrl)) return a.BrowserDownloadUrl;
            }
            return null;
        }

        // True if we already attempted the same target version inside
        // the cooldown window. Prevents the auto-installer loop when a
        // build's stamped assembly version doesn't actually match the
        // tag (or the install otherwise can't take).
        private static bool JustTriedThisVersion(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            var lastTarget = AppSettings.Get(SettingKey_LastInstallTarget);
            if (string.IsNullOrEmpty(lastTarget)) return false;
            if (!string.Equals(lastTarget, candidate, StringComparison.OrdinalIgnoreCase)) return false;
            var lastAtRaw = AppSettings.Get(SettingKey_LastInstallAt);
            if (string.IsNullOrEmpty(lastAtRaw)) return false;
            if (!DateTime.TryParse(lastAtRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastAt)) return false;
            return (DateTime.UtcNow - lastAt) < ReinstallCooldown;
        }

        private static bool IsNewerVersion(string? remote, string local)
        {
            if (string.IsNullOrWhiteSpace(remote)) return false;
            try
            {
                return new Version(Pad(remote)) > new Version(Pad(local));
            }
            catch
            {
                return false;
            }
            // System.Version requires at least major.minor; pad short
            // versions out to major.minor.build so "1.0" parses and
            // compares correctly against "1.0.5".
            static string Pad(string v)
            {
                var parts = v.Split('.');
                if (parts.Length >= 3) return v;
                var padded = new string[3];
                for (int i = 0; i < 3; i++) padded[i] = i < parts.Length ? parts[i] : "0";
                return string.Join('.', padded);
            }
        }

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

        // ── GitHub Releases JSON shape ─────────────────────────
        // Only the fields we need; System.Text.Json drops the rest.
        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("body")]     public string? Body { get; set; }
            [JsonPropertyName("assets")]   public List<GitHubAsset>? Assets { get; set; }
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]                 public string? Name { get; set; }
            [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        }
    }
}
