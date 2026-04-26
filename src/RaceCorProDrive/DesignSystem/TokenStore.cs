using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.UI;
using RaceCorProDrive.Support;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Runtime design tokens for RaceCor Pro Drive.
    ///
    /// Fetches /api/v1/tokens/native?format=json on app launch, caches to the
    /// app's LocalCacheFolder, and updates an in-memory <see cref="ResolvedTokens"/>.
    /// UI code reads <see cref="TokenStore.Instance.Resolved"/> with fallback to
    /// the compile-time <see cref="Tokens"/> static class.
    ///
    /// Token JSON shape (see apps/web/src/lib/tokens/sd-native.ts):
    ///   { "colors": { "bg": "#0a0a14", ... }, "typography": { ... },
    ///     "fontSize": { "xs": 10, ... }, "layout": { "headerHeight": 41, ... } }
    /// </summary>
    public sealed class TokenStore
    {
        private static readonly Lazy<TokenStore> _instance = new(() => new TokenStore());
        public static TokenStore Instance => _instance.Value;

        public ResolvedTokens Resolved { get; private set; } = ResolvedTokens.Empty;
        public TokenSource Source { get; private set; } = TokenSource.Defaults;
        public DateTime? LastFetched { get; private set; }

        public event EventHandler? TokensChanged;

        private readonly string _cachePath;
        private readonly HttpClient _http = new();

        private TokenStore()
        {
            _cachePath = Path.Combine(AppSettings.CacheDir, "racecor-tokens.json");
            LoadFromDiskIfAvailable();
        }

        /// <summary>
        /// Fetch latest tokens from the server and update in-memory + disk cache.
        /// Safe to call repeatedly — a failed fetch leaves previous tokens in place.
        /// </summary>
        public async Task LoadOrFetchAsync(string baseUrl)
        {
            try
            {
                var url = $"{baseUrl.TrimEnd('/')}/api/v1/tokens/native?format=json";
                var json = await _http.GetStringAsync(url);
                var decoded = JsonSerializer.Deserialize<ResolvedTokens>(json, JsonOpts);
                if (decoded is null) return;
                Resolved = decoded;
                Source = TokenSource.Network;
                LastFetched = DateTime.UtcNow;
                await File.WriteAllTextAsync(_cachePath, json);
                TokensChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Token fetch failed, using {Source}: {ex.Message}");
            }
        }

        private void LoadFromDiskIfAvailable()
        {
            if (!File.Exists(_cachePath)) return;
            try
            {
                var json = File.ReadAllText(_cachePath);
                var decoded = JsonSerializer.Deserialize<ResolvedTokens>(json, JsonOpts);
                if (decoded is null) return;
                Resolved = decoded;
                Source = TokenSource.Disk;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to parse cached tokens: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        // ── Lookup helpers ──────────────────────────────────────────

        public Color ColorFor(string key, Color fallback)
        {
            if (Resolved.Colors.TryGetValue(key, out var raw))
            {
                return ColorHelper.Parse(raw) ?? fallback;
            }
            return fallback;
        }

        public double SizeFor(string key, double fallback)
        {
            if (Resolved.FontSize.TryGetValue(key, out var v)) return v;
            if (Resolved.Layout.TryGetValue(key, out v)) return v;
            return fallback;
        }

        public string FontFor(string key, string fallback)
        {
            return Resolved.Typography.TryGetValue(key, out var v) ? v : fallback;
        }
    }

    public enum TokenSource { Defaults, Disk, Network }

    public sealed class ResolvedTokens
    {
        public Dictionary<string, string> Colors { get; set; } = new();
        public Dictionary<string, string> Typography { get; set; } = new();
        public Dictionary<string, double> FontSize { get; set; } = new();
        public Dictionary<string, double>? FontWeight { get; set; }
        public Dictionary<string, double>? LineHeight { get; set; }
        public Dictionary<string, double> Layout { get; set; } = new();

        public static ResolvedTokens Empty { get; } = new();
    }

    /// <summary>
    /// Parse a token color string (#RRGGBB, #RRGGBBAA, or rgba(r,g,b,a)) to
    /// a WinUI <see cref="Color"/>. Returns null on unrecognized format.
    /// </summary>
    public static class ColorHelper
    {
        public static Color? Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();

            if (s.StartsWith("#"))
            {
                s = s.Substring(1);
                if (s.Length == 6)
                {
                    return Color.FromArgb(0xFF,
                        Convert.ToByte(s.Substring(0, 2), 16),
                        Convert.ToByte(s.Substring(2, 2), 16),
                        Convert.ToByte(s.Substring(4, 2), 16));
                }
                if (s.Length == 8)
                {
                    // Convention in our token DB is #RRGGBBAA (web order).
                    return Color.FromArgb(
                        Convert.ToByte(s.Substring(6, 2), 16),
                        Convert.ToByte(s.Substring(0, 2), 16),
                        Convert.ToByte(s.Substring(2, 2), 16),
                        Convert.ToByte(s.Substring(4, 2), 16));
                }
                return null;
            }

            // rgba(r, g, b, a) — a may be fractional
            if (s.StartsWith("rgba", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var open = s.IndexOf('(');
                var close = s.LastIndexOf(')');
                if (open < 0 || close < 0) return null;
                var parts = s.Substring(open + 1, close - open - 1).Split(',');
                try
                {
                    byte r = (byte)Math.Clamp(int.Parse(parts[0].Trim()), 0, 255);
                    byte g = (byte)Math.Clamp(int.Parse(parts[1].Trim()), 0, 255);
                    byte b = (byte)Math.Clamp(int.Parse(parts[2].Trim()), 0, 255);
                    byte a = 0xFF;
                    if (parts.Length > 3)
                    {
                        var af = double.Parse(parts[3].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        a = (byte)Math.Clamp((int)Math.Round(af * 255), 0, 255);
                    }
                    return Color.FromArgb(a, r, g, b);
                }
                catch { return null; }
            }

            return null;
        }
    }
}
