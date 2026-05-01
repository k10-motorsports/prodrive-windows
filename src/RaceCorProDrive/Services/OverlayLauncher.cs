using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Spawns + tracks the Electron HUD process. The HUD ships next to
    /// this app under <c>Overlay\RaceCorOverlay.exe</c>, so there's no
    /// discovery step — <see cref="ResolveBinary"/> returns the
    /// bundled path. If that file is missing (dev runs where the HUD
    /// hasn't been built yet) we surface that as <see cref="IsAvailable"/>
    /// so the UI can hide the launcher button instead of throwing on
    /// click.
    /// </summary>
    public sealed class OverlayLauncher : INotifyPropertyChanged
    {
        public static OverlayLauncher Shared { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Resolved path to the bundled HUD binary, or null if it isn't installed.</summary>
        public string? BinaryPath { get; }
        public bool IsAvailable => BinaryPath != null && File.Exists(BinaryPath);

        private Process? _process;
        public bool IsRunning
        {
            get => _process is { HasExited: false };
        }

        private OverlayLauncher()
        {
            BinaryPath = ResolveBinary();
        }

        /// <summary>
        /// Spawn the HUD if it isn't already running. Returns the live
        /// process (whether freshly started or pre-existing).
        /// </summary>
        public Process? Launch()
        {
            if (!IsAvailable)
            {
                Debug.WriteLine("[OverlayLauncher] HUD binary not bundled — install includes only the host.");
                return null;
            }
            if (IsRunning) return _process;

            var info = new ProcessStartInfo
            {
                FileName = BinaryPath!,
                WorkingDirectory = Path.GetDirectoryName(BinaryPath!),
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            try
            {
                var proc = Process.Start(info);
                if (proc == null) return null;
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, __) =>
                {
                    _process = null;
                    Raise(nameof(IsRunning));
                };
                _process = proc;
                Raise(nameof(IsRunning));
                return proc;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OverlayLauncher] launch failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Ask the HUD to close. Tries a graceful main-window close
        /// first, then escalates to <c>Kill</c> if the process is
        /// still around after a short grace period.
        /// </summary>
        public void Stop()
        {
            if (_process is not { HasExited: false } proc) return;
            try
            {
                if (!proc.CloseMainWindow())
                {
                    proc.Kill(entireProcessTree: true);
                }
                else if (!proc.WaitForExit(2_000))
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OverlayLauncher] stop failed: {ex.Message}");
            }
            finally
            {
                _process = null;
                Raise(nameof(IsRunning));
            }
        }

        // ── Launch-on-login (HKCU\...\Run) ──

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "RaceCorProDrive";

        /// <summary>
        /// Reflects whether the host is registered to start with
        /// Windows. UI binds to this so the toggle reflects state if
        /// the user removes the entry via Task Manager.
        /// </summary>
        public bool IsLaunchOnLoginEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(RunValueName) is string;
            }
        }

        /// <summary>
        /// Toggle the HKCU Run entry. Stores the host's own EXE — when
        /// the user signs in, Windows launches the host; the host then
        /// reads <see cref="OverlaySettings.WinUIAutoLaunchHud"/> and
        /// either spawns the HUD immediately or waits for a manual
        /// click. Two layers of toggle so "open the host on login" and
        /// "spawn the HUD when the host opens" are independent.
        /// </summary>
        public void SetLaunchOnLogin(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)!;
            if (enabled)
            {
                var hostExe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(hostExe)) return;
                key.SetValue(RunValueName, $"\"{hostExe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            Raise(nameof(IsLaunchOnLoginEnabled));
        }

        // ── Helpers ──

        /// <summary>
        /// Resolves the bundled HUD binary in this priority order:
        ///   1. Production install: <c>{BaseDir}\Overlay\RaceCorOverlay.exe</c>
        ///      (Inno Setup drops the unpacked electron tree there).
        ///   2. Dev fallback: walk up parent dirs looking for a sibling
        ///      <c>prodrive-overlay\dist\win-unpacked\RaceCorOverlay.exe</c>
        ///      so the FAB works for <c>dotnet run</c> from the source
        ///      tree as long as the overlay's been built once via
        ///      <c>npm run build:win</c> (or <c>dev-build.ps1 -IncludeOverlay</c>).
        ///   3. Env var override <c>RACECOR_OVERLAY_PATH</c> for one-off
        ///      dev setups where the overlay lives in a non-standard spot.
        /// </summary>
        private static string? ResolveBinary()
        {
            // electron-builder names the Windows exe per `productName`
            // in the overlay's electron-builder.yml.
            const string OverlayExe = "RaceCorProDriveOverlay.exe";

            // 1. Production install layout — Overlay\ next to the host.
            var primary = Path.Combine(AppContext.BaseDirectory, "Overlay", OverlayExe);
            if (File.Exists(primary)) return primary;

            // 3. Env var override (checked early so it always wins
            //    over the auto-discovery walk if the user set it).
            var envOverride = Environment.GetEnvironmentVariable("RACECOR_OVERLAY_PATH");
            if (!string.IsNullOrEmpty(envOverride) && File.Exists(envOverride))
            {
                return envOverride;
            }

            // 2. Dev fallback — walk up from BaseDirectory looking for
            //    a sibling prodrive-overlay/dist/win-unpacked/ tree.
            //    Capped at 8 levels so we don't grovel up to the drive
            //    root on production installs that lack the overlay.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; depth < 8 && dir != null; depth++)
            {
                var candidate = Path.Combine(
                    dir.FullName, "prodrive-overlay", "dist", "win-unpacked", OverlayExe);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
