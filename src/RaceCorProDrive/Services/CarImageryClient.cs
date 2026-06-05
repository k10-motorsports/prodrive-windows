using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Minimal read-only client for the prodrive car-imagery API
    /// (https://images.racecor.io) — the shared manufacturer-press + Wikimedia (CC)
    /// car photo library that also backs commentary backgrounds on the other apps.
    /// Used by the screensaver to pull a random wall of car photography. Public +
    /// CORS-open, so no auth. See the `car-imagery-api` skill for the full contract.
    /// </summary>
    public static class CarImageryClient
    {
        private const string Api = "https://images.racecor.io/api/v1";

        // One shared client for the app's lifetime (avoids socket exhaustion).
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly JsonSerializerOptions JsonOpts =
            new() { PropertyNameCaseInsensitive = true };

        /// <summary>A single car photo: a directly-loadable image URL + attribution.</summary>
        public sealed record CarShot(string Url, string DisplayName, string? Credit);

        /// <summary>
        /// Fetch up to <paramref name="count"/> random car photos (distinct cars where
        /// possible) at the given render width. With no <paramref name="category"/> this
        /// spans the whole library (race + heritage + showcase); pass "race" to limit to
        /// the actual iRacing race cars. Returns an EMPTY list on any failure; callers
        /// degrade gracefully rather than throwing.
        /// </summary>
        public static async Task<IReadOnlyList<CarShot>> GetRandomAsync(
            int count, int width, string? category = null, CancellationToken ct = default)
        {
            try
            {
                var url = $"{Api}/random?count={count}&w={width}";
                if (!string.IsNullOrEmpty(category)) url += $"&category={category}";
                var resp = await Http.GetFromJsonAsync<RandomResponse>(url, JsonOpts, ct)
                    .ConfigureAwait(false);

                var images = resp?.Images;
                if (images is null) return Array.Empty<CarShot>();

                var shots = new List<CarShot>(images.Count);
                foreach (var img in images)
                {
                    if (string.IsNullOrEmpty(img.Url)) continue;
                    shots.Add(new CarShot(img.Url!, img.DisplayName ?? string.Empty, img.Credit));
                }
                return shots;
            }
            catch
            {
                // Network down, API hiccup, JSON change — never crash the screensaver.
                return Array.Empty<CarShot>();
            }
        }

        private sealed class RandomResponse
        {
            [JsonPropertyName("images")] public List<RandomImage>? Images { get; set; }
        }

        private sealed class RandomImage
        {
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
            [JsonPropertyName("credit")] public string? Credit { get; set; }
        }
    }
}
