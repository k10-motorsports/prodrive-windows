using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Owns the round-trip between the WinUI host's settings UI and
    /// the JSON file the Electron HUD reads. Bidirectional:
    ///   - Host writes (user edits a control) → file → HUD's fs.watch
    ///     picks it up and re-applies live.
    ///   - HUD writes (user moves the panel via Ctrl+Shift+S) → file →
    ///     host's <see cref="FileSystemWatcher"/> picks it up and
    ///     refreshes the bound page.
    ///
    /// The settings file lives at
    ///   %APPDATA%\K10 Motorsports\overlay-settings.json
    /// — same path the HUD reads/writes via Electron's
    /// <c>app.getPath('userData')</c>. The HUD's <c>main.js</c> calls
    /// <c>app.setName('K10 Motorsports')</c>, which is what determines the
    /// userData folder on Windows (NOT package.json's <c>name</c>, which
    /// is "racecor-overlay"). If you change the name on either side, the
    /// other side must change too or settings stop propagating.
    /// </summary>
    public sealed class OverlaySettingsService
    {
        public static OverlaySettingsService Shared { get; } = new();

        public event EventHandler<OverlaySettings>? Changed;

        private readonly string _path;
        private FileSystemWatcher? _watcher;
        private DateTime _lastSelfWriteUtc = DateTime.MinValue;
        private static readonly TimeSpan SelfWriteIgnoreWindow = TimeSpan.FromMilliseconds(750);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization
                .JsonIgnoreCondition.WhenWritingNull,
        };

        private OverlaySettingsService()
        {
            // %APPDATA%\K10 Motorsports\overlay-settings.json — must match
            // Electron's `app.getPath('userData')`, which is driven by the
            // overlay's `app.setName('K10 Motorsports')` call in main.js.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "K10 Motorsports");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "overlay-settings.json");
        }

        /// <summary>Path the HUD also reads from. Useful for diagnostics + tests.</summary>
        public string FilePath => _path;

        /// <summary>
        /// Read the current state from disk. If the file is missing or
        /// unreadable, returns defaults so the SettingsPage has
        /// something to bind to.
        /// </summary>
        public OverlaySettings Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var parsed = JsonSerializer.Deserialize<OverlaySettings>(json, JsonOpts)
                                 ?? new OverlaySettings();
                    return OverlaySettingsDefaults.Apply(parsed);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OverlaySettings] read failed: {ex.Message}");
            }
            return OverlaySettingsDefaults.Apply(new OverlaySettings());
        }

        /// <summary>
        /// Persist the current settings. Writes through a
        /// <c>.tmp</c> file + atomic rename so the HUD never observes
        /// a half-written JSON document on its watcher tick. Marks the
        /// timestamp so our own watcher ignores the round-trip.
        /// </summary>
        public async Task SaveAsync(OverlaySettings settings)
        {
            try
            {
                _lastSelfWriteUtc = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(settings, JsonOpts);
                var tmp = _path + ".tmp";
                // ConfigureAwait(false) is load-bearing: callers may block on this
                // via GetAwaiter().GetResult() from the UI thread (see
                // RecordingControlServer.PersistPortToSettings, invoked from
                // App.OnLaunched). Without it the continuation is posted back to the
                // UI dispatcher that's already blocked in GetResult() → deadlock, and
                // the app hangs on a blank window. Resuming off the UI context keeps
                // the write self-contained on the thread pool.
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OverlaySettings] save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Start watching the file. Fires <see cref="Changed"/> when
        /// something OTHER than this service writes — i.e. when the
        /// HUD persists a change the user made via its in-game hotkey
        /// (move/resize/hide). Self-writes are suppressed for
        /// <see cref="SelfWriteIgnoreWindow"/> so we don't ping-pong.
        /// </summary>
        public void StartWatching()
        {
            if (_watcher != null) return;
            try
            {
                var dir = Path.GetDirectoryName(_path)!;
                var name = Path.GetFileName(_path);
                _watcher = new FileSystemWatcher(dir, name)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OverlaySettings] watch failed: {ex.Message}");
            }
        }

        public void StopWatching()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        private void OnFileChanged(object _, FileSystemEventArgs __)
        {
            // Inside the self-write window? Almost certainly our own
            // save round-tripping back. Skip to avoid the listener
            // firing on every keystroke.
            if (DateTime.UtcNow - _lastSelfWriteUtc < SelfWriteIgnoreWindow) return;

            // Tiny delay — file-system events can fire before the
            // writer's stream has flushed. Re-reading too eagerly
            // races and gets a partial JSON.
            Thread.Sleep(50);
            try
            {
                var snapshot = Load();
                Changed?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OverlaySettings] watcher reload failed: {ex.Message}");
            }
        }
    }
}
