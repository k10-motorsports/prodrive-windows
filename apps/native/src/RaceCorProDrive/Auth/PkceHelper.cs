using System.Security.Cryptography;
using System.Text;

namespace RaceCorProDrive.Auth
{
    /// <summary>
    /// PKCE (Proof Key for Public Clients) helper for OAuth 2.0 authorization code flow.
    /// </summary>
    public static class PkceHelper
    {
        /// <summary>
        /// Generates a random code verifier (64 bytes, base64url encoded).
        /// </summary>
        public static string GenerateCodeVerifier()
        {
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[64];
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Generates the code challenge from a verifier using SHA256.
        /// </summary>
        public static string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                return Base64UrlEncode(hash);
            }
        }

        /// <summary>
        /// Base64url encodes a byte array (no padding).
        /// </summary>
        private static string Base64UrlEncode(byte[] input)
        {
            var base64 = Convert.ToBase64String(input);
            return base64
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
    }
}
