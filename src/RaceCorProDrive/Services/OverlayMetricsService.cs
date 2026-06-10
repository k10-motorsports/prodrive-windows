using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Read side of the metrics sidecar (<c>overlay-metrics.json</c>,
    /// next to overlay-settings.json). The Electron overlay is the only
    /// writer — it measures every module/group after each layout apply
    /// and writes atomically (tmp + rename). The Visual tab's editor
    /// watches this to keep its proxies pixel-exact. One writer per
    /// file by design; see docs/overlay-layout-v2.md §1.3.
    /// </summary>
    public sealed class OverlayMetricsService
    {
        public static OverlayMetricsService Shared { get; } = new();

        public event EventHandler<OverlayMetrics>? Changed;

        private readonly string _path;
        private FileSystemWatcher? _watcher;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private OverlayMetricsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _path = Path.Combine(appData, "K10 Motorsports", "overlay-metrics.json");
        }

        public string FilePath => _path;

        /// <summary>Latest metrics, or null when the overlay has never
        /// run with a v2 layout (callers fall back to registry nominals).</summary>
        public OverlayMetrics? Load()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                return Parse(File.ReadAllText(_path));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OverlayMetrics] read failed: {ex.Message}");
                return null;
            }
        }

        public static OverlayMetrics? Parse(string json)
            => JsonSerializer.Deserialize<OverlayMetrics>(json, JsonOpts);

        public void StartWatching()
        {
            if (_watcher != null) return;
            try
            {
                var dir = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(dir);
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileChanged; // atomic tmp→rename lands here
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OverlayMetrics] watch failed: {ex.Message}");
            }
        }

        public void StopWatching()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private void OnFileChanged(object _, FileSystemEventArgs __)
        {
            // Same flush-race guard as the settings watcher.
            Thread.Sleep(50);
            var snapshot = Load();
            if (snapshot != null) Changed?.Invoke(this, snapshot);
        }
    }
}
