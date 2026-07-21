using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RaceCorProDrive.Api
{
    /// <summary>
    /// Wraps the eight web-api <c>/api/v1/calc/*</c> endpoints.
    ///
    /// Spec: <c>prodrive-server/docs/native-app-integration.md</c>.
    /// Per-screen REST (<c>prodrive.racecor.io/api/v1/*</c>, served by
    /// <see cref="ApiClient.Shared"/>) is the default for fetching
    /// user-scoped data; this client is the escape hatch for what-if
    /// flows — windowed DNA, scoped Mastery, race-summary against a
    /// session not yet in the DB, etc.
    ///
    /// Auth: the Phase 9b plan is to gate calc behind the same Bearer
    /// the per-screen surface uses. We send it prophylactically today
    /// (web-api ignores it for now) so when the gate flips on, no
    /// client release is needed. AuthManager should call
    /// <see cref="SetBearerToken"/> here whenever it sets the token on
    /// <c>ApiClient.Shared</c> — same JWT, two surfaces.
    /// </summary>
    public sealed class CalcClient
    {
        public static CalcClient Shared { get; } = new();

        // Calc endpoints are served under the apex host, NOT a dedicated
        // `api.` subdomain. The zone's TLS cert covers `racecor.io` and
        // `*.racecor.io` only — a one-label-deep wildcard. `prodrive.racecor.io`
        // matches it; `api.prodrive.racecor.io` (two labels deep) does not, so
        // that host fails the TLS handshake and every calc call throws. Keep
        // this on the apex unless a cert that actually covers the api subdomain
        // is provisioned (Cloudflare ACM / Total TLS). Same origin ApiClient uses.
        private const string BaseUrl = "https://prodrive.racecor.io";

        private readonly HttpClient _http;
        private string? _bearerToken;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public CalcClient() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }) { }

        public CalcClient(HttpClient httpClient)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void SetBearerToken(string? token) => _bearerToken = token;

        // ─── 1. Driver DNA ───────────────────────────────────────
        public Task<DriverDnaResponse> FetchDriverDnaAsync(
            IEnumerable<DnaSession> sessions,
            IEnumerable<DnaRating> ratings,
            CancellationToken ct = default)
            => PostAsync<DriverDnaResponse>("/api/v1/calc/dna",
                new { sessions, ratings }, ct);

        // ─── 2. Mastery ──────────────────────────────────────────
        public Task<MasteryResponse> FetchMasteryAsync(
            IEnumerable<MasterySession> sessions,
            CancellationToken ct = default)
            => PostAsync<MasteryResponse>("/api/v1/calc/mastery",
                new { sessions }, ct);

        // ─── 3. Moments ──────────────────────────────────────────
        public Task<MomentsResponse> FetchMomentsAsync(
            IEnumerable<MomentSession> sessions,
            IEnumerable<MomentRating> ratings,
            CancellationToken ct = default)
            => PostAsync<MomentsResponse>("/api/v1/calc/moments",
                new { sessions, ratings }, ct);

        // ─── 4. Scatter buckets ──────────────────────────────────
        public Task<ScatterResponse> FetchScatterAsync(
            IEnumerable<ScatterSession> sessions,
            string? timeZone = null,
            CancellationToken ct = default)
        {
            object body = string.IsNullOrEmpty(timeZone)
                ? new { sessions }
                : new { sessions, timeZone };
            return PostAsync<ScatterResponse>("/api/v1/calc/scatter", body, ct);
        }

        // ─── 5. When-engine ──────────────────────────────────────
        public Task<WhenResponse> FetchWhenAsync(
            IEnumerable<WhenSession> sessions,
            IEnumerable<WhenRating> ratings,
            string? timeZone = null,
            CancellationToken ct = default)
        {
            object body = string.IsNullOrEmpty(timeZone)
                ? new { sessions, ratings }
                : new { sessions, ratings, timeZone };
            return PostAsync<WhenResponse>("/api/v1/calc/when", body, ct);
        }

        // ─── 6. Race-Now verdict ─────────────────────────────────
        // Recommended cadence: once on app start, then once every 5–10
        // minutes (or on hour rollover). Verdict only changes on hour
        // boundaries; faster polling burns the user's data plan.
        public Task<RaceNowResponse> FetchRaceNowAsync(
            JsonElement? profile = null,
            DateTimeOffset? now = null,
            string? timeZone = null,
            IEnumerable<RaceNowSessionInput>? sessions = null,
            CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> { ["profile"] = profile };
            if (now.HasValue) body["now"] = now.Value.UtcDateTime;
            if (!string.IsNullOrEmpty(timeZone)) body["timeZone"] = timeZone;
            if (sessions != null) body["sessions"] = sessions;
            return PostAsync<RaceNowResponse>("/api/v1/calc/race-now", body, ct);
        }

        // ─── 7. Race summary ─────────────────────────────────────
        public Task<RaceSummaryResponse> FetchRaceSummaryAsync(
            RaceSummaryRequest request,
            CancellationToken ct = default)
            => PostAsync<RaceSummaryResponse>("/api/v1/calc/race-summary",
                request, ct);

        // ─── 8. Next race ideas ──────────────────────────────────
        public Task<NextRaceIdeasResponse> FetchNextRaceIdeasAsync(
            NextRaceIdeasRequest request,
            CancellationToken ct = default)
            => PostAsync<NextRaceIdeasResponse>("/api/v1/calc/next-race-ideas",
                request, ct);

        // ─── Core POST helper ────────────────────────────────────
        private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path) { Content = content };

            if (!string.IsNullOrEmpty(_bearerToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
            }

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var preview = string.Empty;
                try { preview = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { }
                if (preview.Length > 500) preview = preview[..500];
                throw new CalcClientException(path, (int)resp.StatusCode, preview);
            }

            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);
            return result ?? throw new InvalidOperationException($"calc {path}: deserialised to null");
        }
    }

    public sealed class CalcClientException : Exception
    {
        public string Path { get; }
        public int StatusCode { get; }
        public string ResponseBodyPreview { get; }

        public CalcClientException(string path, int statusCode, string preview)
            : base($"calc-client {path} {statusCode}: {preview}")
        {
            Path = path;
            StatusCode = statusCode;
            ResponseBodyPreview = preview;
        }
    }
}
