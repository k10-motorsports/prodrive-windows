using Windows.Security.Credentials;
using System.Text.Json;

namespace RaceCorProDrive.Auth
{
    /// <summary>
    /// Secure token storage using Windows Credential Manager (PasswordVault).
    /// </summary>
    public class TokenStore
    {
        private const string ResourceName = "RaceCorProDrive";
        private const string TokenCredentialName = "auth_tokens";
        private readonly PasswordVault _vault = new();

        public class TokenData
        {
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        /// <summary>
        /// Saves tokens to the secure credential store.
        /// </summary>
        public void SaveTokens(string accessToken, string refreshToken, DateTime expiresAt)
        {
            var tokenData = new TokenData
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt
            };

            var json = JsonSerializer.Serialize(tokenData);

            try
            {
                var existing = _vault.FindAllByResource(ResourceName);
                foreach (var cred in existing)
                {
                    _vault.Remove(cred);
                }
            }
            catch
            {
                // No existing credentials
            }

            var credential = new PasswordCredential(ResourceName, TokenCredentialName, json);
            _vault.Add(credential);
        }

        /// <summary>
        /// Retrieves tokens from the secure credential store.
        /// Returns null if no tokens are stored.
        /// </summary>
        public TokenData? LoadTokens()
        {
            try
            {
                var creds = _vault.FindAllByResource(ResourceName);
                var tokenCred = creds.FirstOrDefault(c => c.UserName == TokenCredentialName);

                if (tokenCred == null)
                    return null;

                tokenCred.RetrievePassword();
                var json = tokenCred.Password;
                return JsonSerializer.Deserialize<TokenData>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Clears all stored tokens.
        /// </summary>
        public void ClearTokens()
        {
            try
            {
                var creds = _vault.FindAllByResource(ResourceName);
                foreach (var cred in creds)
                {
                    _vault.Remove(cred);
                }
            }
            catch
            {
                // Already cleared
            }
        }

        /// <summary>
        /// Checks if tokens exist and are still valid.
        /// </summary>
        public bool HasValidTokens()
        {
            var tokens = LoadTokens();
            if (tokens == null)
                return false;

            return DateTime.UtcNow < tokens.ExpiresAt;
        }
    }
}
