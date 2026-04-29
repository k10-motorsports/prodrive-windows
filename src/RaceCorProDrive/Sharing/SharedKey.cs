using System;
using System.Security.Cryptography;
using System.Text;

namespace RaceCorProDrive.Sharing
{
    /// <summary>
    /// Derives the LAN-transfer shared key from a Pro Drive bearer token.
    /// Mirrors the Swift derivation in
    /// <c>prodrive-macos/Sources/Sharing/SharedKey.swift</c>.
    ///
    /// Both devices compute the same key from their own bearer token
    /// because both are signed in as the same user. The key never travels
    /// over the wire — only HMACs do.
    ///
    /// Spec: agents/skills/bundle-transfer/SKILL.md (LAN auth section).
    /// </summary>
    public static class SharedKey
    {
        public const string Salt = "prodrive-lan-v1";

        /// <summary>
        /// SHA256(bearerToken || ":" || salt).
        /// </summary>
        public static byte[] Derive(string bearerToken)
        {
            var input = $"{bearerToken}:{Salt}";
            return SHA256.HashData(Encoding.UTF8.GetBytes(input));
        }

        /// <summary>
        /// HMAC-SHA256 over <c>bundleId || ":" || timestamp</c>, base64-encoded.
        /// </summary>
        public static string Sign(string bundleId, long timestampMs, byte[] key)
        {
            var payload = Encoding.UTF8.GetBytes($"{bundleId}:{timestampMs}");
            var mac = HMACSHA256.HashData(key, payload);
            return Convert.ToBase64String(mac);
        }
    }
}
