using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RaceCorProDrive.Api;
using Windows.Security.Authentication.Web;

namespace RaceCorProDrive.Auth
{
    /// <summary>
    /// Handles OAuth 2.0 PKCE flow for ProDrive API authentication.
    /// Uses system browser + protocol handler fallback for unpackaged apps.
    ///
    /// Flow:
    /// 1. Generate PKCE verifier + challenge
    /// 2. Open browser to /authorize endpoint
    /// 3. User grants permission
    /// 4. Browser redirects to racecor-prodrive:// with auth code
    /// 5. Protocol handler routes to this service's OnAuthCallback
    /// 6. Exchange code for tokens via /token endpoint
    /// 7. Store tokens securely in PasswordVault
    /// </summary>
    public class AuthService
    {
        private const string ClientId = "racing.k10motorsports.prodrive.racecor.win";
        private const string RedirectUri = "racecor-native://auth";
        private const string BaseUrl = "https://prodrive.racecor.io";

        private readonly TokenStore _tokenStore = new();
        private readonly ApiClient _apiClient = new();
        private User? _currentUser;

        public User? CurrentUser => _currentUser;

        public bool IsAuthenticated
        {
            get
            {
                if (_currentUser != null)
                    return true;

                // Try to load from persisted tokens
                var tokens = _tokenStore.LoadTokens();
                return tokens != null && DateTime.UtcNow < tokens.ExpiresAt;
            }
        }

        /// <summary>
        /// Try the in-app <see cref="WebAuthenticationBroker"/> path
        /// before falling back to the system browser. Returns
        /// <c>true</c> when the broker completes the flow end-to-end
        /// (token exchange + user load), <c>false</c> when it isn't
        /// usable (unpackaged app refusal, callback URI mismatch,
        /// user cancel) — caller should then call
        /// <see cref="SignInAsync"/> for the system-browser path.
        /// </summary>
        /// <remarks>
        /// CLAUDE.md notes the broker is unreliable on unpackaged
        /// builds, but the in-app modal UX is much nicer when it
        /// works (no app switching, no protocol-handler tab dance).
        /// We try it first, gracefully fall back. Once we ship
        /// MSIX-packaged this path becomes the primary one.
        /// </remarks>
        public async Task<bool> SignInWithBrokerAsync()
        {
            var codeVerifier = PkceHelper.GenerateCodeVerifier();
            var codeChallenge = PkceHelper.GenerateCodeChallenge(codeVerifier);
            var state = Guid.NewGuid().ToString("N");

            var startUri = new Uri(
                $"{BaseUrl}/api/plugin-auth/authorize?" +
                $"client_id={ClientId}&" +
                $"code_challenge={codeChallenge}&" +
                $"code_challenge_method=S256&" +
                $"state={state}&" +
                $"redirect_uri={Uri.EscapeDataString(RedirectUri)}");
            var endUri = new Uri(RedirectUri);

            WebAuthenticationResult result;
            try
            {
                result = await WebAuthenticationBroker.AuthenticateAsync(
                    WebAuthenticationOptions.None,
                    startUri,
                    endUri);
            }
            catch (Exception ex)
            {
                // The broker throws on unpackaged apps that lack the
                // WebAuthBroker capability — that's our cue to fall
                // back. Logged so we can correlate against actual
                // user environments.
                Debug.WriteLine($"[AUTH] WebAuthenticationBroker unavailable: {ex.Message}");
                return false;
            }

            switch (result.ResponseStatus)
            {
                case WebAuthenticationStatus.Success:
                    // Broker hands us the full callback URL with
                    // `?code=…&state=…` appended. Reuse the same
                    // callback handler the protocol path uses so the
                    // PKCE + token-exchange path is shared.
                    SaveAuthState(new AuthState { CodeVerifier = codeVerifier, State = state });
                    await OnAuthCallbackAsync(result.ResponseData);
                    return true;

                case WebAuthenticationStatus.UserCancel:
                    Debug.WriteLine("[AUTH] broker: user cancelled");
                    return false;

                default:
                    Debug.WriteLine($"[AUTH] broker error: status={result.ResponseStatus}, code={result.ResponseErrorDetail}");
                    return false;
            }
        }

        /// <summary>
        /// OAuth sign-in with loopback HTTP redirect — the canonical desktop
        /// pattern (gh CLI, dotnet, every modern desktop OAuth client).
        /// We bind a one-shot HttpListener to http://127.0.0.1:&lt;random&gt;/auth,
        /// pass that as redirect_uri, then await the browser's redirect.
        /// Awaits the full token exchange before returning, so callers can
        /// transition to the authed shell synchronously.
        ///
        /// Why not racecor-native://: modern browsers (Chrome/Edge 88+)
        /// silently drop server-initiated redirects to custom URI schemes
        /// when there's no user gesture. Loopback HTTP is just a regular
        /// redirect — works everywhere.
        /// </summary>
        public async Task SignInAsync()
        {
            var codeVerifier = PkceHelper.GenerateCodeVerifier();
            var codeChallenge = PkceHelper.GenerateCodeChallenge(codeVerifier);
            var state = Guid.NewGuid().ToString("N");
            SaveAuthState(new AuthState { CodeVerifier = codeVerifier, State = state });

            var port = GetAvailableLoopbackPort();
            // Advertise "localhost" in redirect_uri (OAuth servers commonly
            // string-match that hostname in their allowlists), but bind the
            // listener to 127.0.0.1 — Windows' HttpListener needs a URL ACL
            // reservation to bind to "localhost", and we don't have admin.
            // Both are equivalent on the wire; the browser resolves the
            // hostname before connecting.
            var redirectUri = $"http://localhost:{port}/auth";
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var authUrl = $"{BaseUrl}/api/plugin-auth/authorize?" +
                $"response_type=code&" +
                $"client_id={ClientId}&" +
                $"code_challenge={codeChallenge}&" +
                $"code_challenge_method=S256&" +
                $"state={state}&" +
                $"redirect_uri={Uri.EscapeDataString(redirectUri)}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                listener.Stop();
                throw new InvalidOperationException($"Failed to open browser: {ex.Message}", ex);
            }

            try
            {
                // Wait for the browser to hit /auth?code=...&state=...
                var ctx = await listener.GetContextAsync();
                var callbackUri = ctx.Request.Url!.ToString();

                // Send a friendly "you can close this" page so the user
                // isn't staring at about:blank wondering if it worked.
                var html =
                    "<!doctype html><html><head><meta charset=utf-8><title>RaceCor Pro Drive</title>" +
                    "<style>body{font-family:-apple-system,Segoe UI,sans-serif;background:#0A0A14;color:#fff;" +
                    "display:flex;align-items:center;justify-content:center;height:100vh;margin:0}" +
                    ".c{text-align:center}h1{font-weight:600;margin:0 0 8px}p{opacity:.7;margin:0}</style></head>" +
                    "<body><div class=c><h1>Sign-in complete</h1><p>You can close this tab and return to RaceCor Pro Drive.</p></div></body></html>";
                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();

                // Exchange the code for tokens (this is the heavy lift —
                // talks to the auth server, hits PasswordVault, etc.).
                await OnAuthCallbackAsync(callbackUri);
            }
            finally
            {
                listener.Stop();
            }
        }

        private static int GetAvailableLoopbackPort()
        {
            // Bind to port 0; OS picks a free ephemeral port. Close, then
            // reuse the number. Tiny race-condition window but in practice
            // fine for local one-shot OAuth.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>
        /// Called by the protocol handler when the user completes authorization.
        /// Expected URI: racecor-prodrive://auth?code=...&state=...
        /// </summary>
        public async Task OnAuthCallbackAsync(string callbackUri)
        {
            try
            {
                var uri = new Uri(callbackUri);
                var queryParams = ParseQueryString(uri.Query);

                if (!queryParams.TryGetValue("code", out var code))
                    throw new InvalidOperationException("No authorization code in callback");

                if (!queryParams.TryGetValue("state", out var state))
                    throw new InvalidOperationException("No state in callback");

                var authState = LoadAuthState();
                if (authState?.State != state)
                    throw new InvalidOperationException("State mismatch");

                // Exchange code for tokens
                var tokenRequest = new
                {
                    grant_type = "authorization_code",
                    code = code,
                    code_verifier = authState.CodeVerifier
                };

                var response = await _apiClient.PostAsync<TokenResponse>(
                    $"{BaseUrl}/api/plugin-auth/token",
                    tokenRequest);

                if (response?.AccessToken == null)
                    throw new InvalidOperationException("No access token in response");

                // Store tokens
                var expiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn);
                _tokenStore.SaveTokens(response.AccessToken, response.RefreshToken ?? "", expiresAt);

                // Fetch and cache current user
                await LoadCurrentUserAsync();

                ClearAuthState();
            }
            catch (Exception ex)
            {
                ClearAuthState();
                throw;
            }
        }

        /// <summary>
        /// Fetches the authenticated user's profile.
        /// </summary>
        private async Task LoadCurrentUserAsync()
        {
            var tokens = _tokenStore.LoadTokens();
            if (tokens?.AccessToken == null)
                return;

            _apiClient.SetAuthToken(tokens.AccessToken);
            // Same JWT, two surfaces: per-screen REST + calc POST.
            // CalcClient sends Bearer prophylactically; web-api ignores it
            // today but Phase 9b will gate calc on it.
            CalcClient.Shared.SetBearerToken(tokens.AccessToken);

            try
            {
                _currentUser = await _apiClient.GetAsync<User>($"{BaseUrl}/api/v1/me");
            }
            catch
            {
                _currentUser = null;
            }
        }

        /// <summary>
        /// Refreshes the access token using the refresh token.
        /// </summary>
        public async Task RefreshTokenAsync()
        {
            var tokens = _tokenStore.LoadTokens();
            if (tokens?.RefreshToken == null)
                throw new InvalidOperationException("No refresh token");

            var tokenRequest = new
            {
                grant_type = "refresh_token",
                refresh_token = tokens.RefreshToken
            };

            var response = await _apiClient.PostAsync<TokenResponse>(
                $"{BaseUrl}/api/plugin-auth/token",
                tokenRequest);

            if (response?.AccessToken == null)
                throw new InvalidOperationException("Failed to refresh token");

            var expiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn);
            _tokenStore.SaveTokens(response.AccessToken, response.RefreshToken ?? "", expiresAt);
            _apiClient.SetAuthToken(response.AccessToken);
            CalcClient.Shared.SetBearerToken(response.AccessToken);
        }

        /// <summary>
        /// Signs out the user and clears all tokens.
        /// </summary>
        public void SignOut()
        {
            _tokenStore.ClearTokens();
            _currentUser = null;
            _apiClient.SetAuthToken(null);
            // Same JWT, two surfaces. Clear both.
            CalcClient.Shared.SetBearerToken(null);
        }

        private class AuthState
        {
            public string CodeVerifier { get; set; } = "";
            public string State { get; set; } = "";
        }

        private void SaveAuthState(AuthState state)
        {
            RaceCorProDrive.Support.AppSettings.Set("auth_state", JsonSerializer.Serialize(state));
        }

        private AuthState? LoadAuthState()
        {
            var json = RaceCorProDrive.Support.AppSettings.Get("auth_state");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonSerializer.Deserialize<AuthState>(json); }
            catch { return null; }
        }

        private void ClearAuthState()
        {
            RaceCorProDrive.Support.AppSettings.Remove("auth_state");
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(query))
                return result;

            var pairs = query.TrimStart('?').Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    result[parts[0]] = Uri.UnescapeDataString(parts[1]);
                }
            }
            return result;
        }
    }

    public class TokenResponse
    {
        // OAuth 2.0 spec returns snake_case (access_token, refresh_token,
        // token_type, expires_in). The shared JsonSerializerOptions on
        // ApiClient uses CamelCase which would silently leave these null,
        // which is exactly the "no token detected" failure mode.
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 3600;
    }

    public class User
    {
        public string? Id { get; set; }
        public string? DiscordId { get; set; }
        public string? DiscordUsername { get; set; }
        public string? DiscordDisplayName { get; set; }
        public string? DiscordAvatar { get; set; }
        public string? CustomLogoUrl { get; set; }
        public string? Email { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
