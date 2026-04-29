using System.Text;
using System.Text.Json;
using RaceCorProDrive.Auth;
using RaceCorProDrive.Support;

namespace RaceCorProDrive.Api
{
    /// <summary>
    /// HTTP client for ProDrive API.
    /// Handles Bearer token attachment, automatic refresh on 401, and JSON serialization.
    /// Mirrors the shared-shape `APIClient.shared` used on macOS / iOS / tvOS — one
    /// process-wide instance so the bearer token set after sign-in is visible to every
    /// consumer without threading the client through the view hierarchy.
    /// </summary>
    public class ApiClient
    {
        /// <summary>
        /// Process-wide instance. Views / pollers read
        /// <see cref="Shared"/> the same way Swift code reads
        /// <c>APIClient.shared</c>. New instances via <c>new</c> are
        /// discouraged; they drop the bearer token set after
        /// sign-in.
        /// </summary>
        public static ApiClient Shared { get; } = new();

        private const string BaseUrl = "https://prodrive.racecor.io";

        private readonly HttpClient _httpClient;
        private readonly TokenStore _tokenStore = new();
        private string? _currentAuthToken;
        private bool _isRefreshing = false;

        public ApiClient()
        {
            var handler = new HttpClientHandler();
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Load existing token if available
            var tokens = _tokenStore.LoadTokens();
            if (tokens?.AccessToken != null)
            {
                _currentAuthToken = tokens.AccessToken;
            }
        }

        /// <summary>
        /// Sets the authorization token (called after successful auth).
        /// </summary>
        public void SetAuthToken(string? token)
        {
            _currentAuthToken = token;
        }

        /// <summary>
        /// Issues a GET request and deserializes to T.
        /// Handles 401 with automatic token refresh + retry.
        /// </summary>
        public async Task<T?> GetAsync<T>(string url)
        {
            var response = await SendAsync(HttpMethod.Get, url, null);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }

            throw new HttpRequestException($"GET {url} failed: {response.StatusCode}");
        }

        /// <summary>
        /// Issues a POST request and deserializes to T.
        /// Handles 401 with automatic token refresh + retry.
        /// </summary>
        public async Task<T?> PostAsync<T>(string url, object? body)
        {
            var json = body != null ? JsonSerializer.Serialize(body, JsonOptions) : null;
            var content = json != null ? new StringContent(json, Encoding.UTF8, "application/json") : null;

            var response = await SendAsync(HttpMethod.Post, url, content);
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(responseJson, JsonOptions);
            }

            throw new HttpRequestException($"POST {url} failed: {response.StatusCode}");
        }

        /// <summary>
        /// Core request handler with automatic 401 + refresh + retry logic.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content)
        {
            var request = new HttpRequestMessage(method, url);
            request.Content = content;

            // Attach Bearer token
            if (!string.IsNullOrEmpty(_currentAuthToken))
            {
                request.Headers.Add("Authorization", $"Bearer {_currentAuthToken}");
            }

            var response = await _httpClient.SendAsync(request);

            // If 401 and we have a refresh token, try once
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !_isRefreshing)
            {
                _isRefreshing = true;
                try
                {
                    var authService = new AuthService();
                    await authService.RefreshTokenAsync();

                    // Retry with new token
                    var retryRequest = new HttpRequestMessage(method, url);
                    retryRequest.Content = content;
                    if (!string.IsNullOrEmpty(_currentAuthToken))
                    {
                        retryRequest.Headers.Add("Authorization", $"Bearer {_currentAuthToken}");
                    }

                    response = await _httpClient.SendAsync(retryRequest);
                }
                catch
                {
                    // Refresh failed, return the 401
                }
                finally
                {
                    _isRefreshing = false;
                }
            }

            return response;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // ── Typed endpoint wrappers ──
        // Keep these thin — each call mirrors the equivalent Swift
        // `APIClient` method so the native clients all hit the same
        // endpoints with the same vocabulary.

        /// <summary>GET /api/v1/me — current user row.</summary>
        public Task<Me?> MeAsync() => GetAsync<Me>($"{BaseUrl}/api/v1/me");

        /// <summary>
        /// GET /api/v1/dashboard — full dashboard payload.
        ///
        /// Passes the device's IANA timezone so the server buckets the
        /// returned WhenProfile.byHour / rankedSlots in the user's
        /// local hour-of-week. Without this the server defaults to UTC
        /// and rankedSlots[localHour] returns the wrong row, producing
        /// inverted Race-Now verdicts vs the web dashboard. See
        /// prodrive-server PR #28 for the matching server change.
        ///
        /// .NET on Windows surfaces tzdb names directly when
        /// HasIanaId is true; otherwise convert from the Windows zone
        /// registry (same shape DashboardPage.RefreshRaceNowAsync
        /// already uses to send TZ to /calc/race-now).
        /// </summary>
        public Task<Dashboard?> DashboardAsync(
            Support.DashboardContext context = Support.DashboardContext.Race,
            Support.DashboardLicense license = Support.DashboardLicense.All)
        {
            var tz = TimeZoneInfo.Local.HasIanaId
                ? TimeZoneInfo.Local.Id
                : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)
                    ? iana
                    : "UTC";
            var encodedTz = Uri.EscapeDataString(tz);

            // Context + license carry the user's dashboard view
            // prefs. Defaults match the server's defaults
            // (race + all) so a caller that omits them gets
            // backward-compatible behavior.
            var ctxWire = context.ToWireValue();
            var licWire = license.ToWireValue();
            return GetAsync<Dashboard>(
                $"{BaseUrl}/api/v1/dashboard?tz={encodedTz}&context={ctxWire}&license={licWire}"
            );
        }

        /// <summary>GET /api/v1/sessions — paginated race list.</summary>
        public Task<SessionsResponse?> SessionsAsync(int limit = 50, int offset = 0, string category = "all") =>
            GetAsync<SessionsResponse>(
                $"{BaseUrl}/api/v1/sessions?limit={limit}&offset={offset}&category={category}");

        /// <summary>GET /api/v1/sessions/{id} — single race with laps.</summary>
        public Task<SessionDetailResponse?> SessionAsync(string id) =>
            GetAsync<SessionDetailResponse>($"{BaseUrl}/api/v1/sessions/{id}");
    }
}
