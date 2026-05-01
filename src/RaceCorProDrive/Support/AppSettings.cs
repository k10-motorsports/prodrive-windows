using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RaceCorProDrive.Support
{
    /// <summary>
    /// Per-user key/value store and cache-dir resolver that works for
    /// unpackaged WinUI 3 apps. Windows.Storage.ApplicationData.Current
    /// throws COMException without a package identity, so we persist to
    /// a JSON file under %LOCALAPPDATA%\RaceCorProDrive\.
    /// </summary>
    public static class AppSettings
    {
        private const string AppFolderName = "RaceCorProDrive";

        private static readonly object _gate = new();
        private static readonly Lazy<string> _root = new(() =>
        {
            var p = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
            Directory.CreateDirectory(p);
            return p;
        });

        public static string RootDir => _root.Value;
        public static string CacheDir
        {
            get
            {
                var p = Path.Combine(RootDir, "Cache");
                Directory.CreateDirectory(p);
                return p;
            }
        }
        public static string LogsDir
        {
            get
            {
                var p = Path.Combine(RootDir, "Logs");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        private static string SettingsPath => Path.Combine(RootDir, "settings.json");

        private static ConcurrentDictionary<string, string>? _cache;

        private static ConcurrentDictionary<string, string> Load()
        {
            if (_cache is { } c) return c;
            lock (_gate)
            {
                if (_cache is { } c2) return c2;
                var dict = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    if (File.Exists(SettingsPath))
                    {
                        var json = File.ReadAllText(SettingsPath);
                        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (parsed != null)
                        {
                            foreach (var (k, v) in parsed) dict[k] = v;
                        }
                    }
                }
                catch { /* corrupt file → start clean */ }
                _cache = dict;
                return dict;
            }
        }

        public static string? Get(string key)
        {
            return Load().TryGetValue(key, out var v) ? v : null;
        }

        public static void Set(string key, string? value)
        {
            var dict = Load();
            if (value is null) dict.TryRemove(key, out _);
            else dict[key] = value;
            Save();
        }

        public static void Remove(string key)
        {
            Load().TryRemove(key, out _);
            Save();
        }

        public static bool TryGetValue(string key, out string value)
        {
            var dict = Load();
            if (dict.TryGetValue(key, out var v)) { value = v; return true; }
            value = ""; return false;
        }

        /// <summary>
        /// Boolean setting accessor with a default for first-run /
        /// missing keys. Round-trips as "true"/"false" strings via
        /// the existing string store. Used for settings that the
        /// upcoming Settings page will eventually expose to the user.
        /// </summary>
        public static bool GetBool(string key, bool defaultValue)
        {
            var raw = Get(key);
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            return bool.TryParse(raw, out var b) ? b : defaultValue;
        }

        public static void SetBool(string key, bool value)
            => Set(key, value ? "true" : "false");

        private static void Save()
        {
            lock (_gate)
            {
                try
                {
                    var dict = Load();
                    var snapshot = new Dictionary<string, string>(dict);
                    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SettingsPath, json);
                }
                catch { /* persistence failure shouldn't crash the app */ }
            }
        }
    }
}
