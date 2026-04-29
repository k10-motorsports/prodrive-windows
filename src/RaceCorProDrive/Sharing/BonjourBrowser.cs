using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zeroconf;

namespace RaceCorProDrive.Sharing
{
    /// <summary>
    /// One-shot Bonjour discovery for `_prodrive._tcp` services on the LAN.
    /// Triggered by the "Send to Mac" UX — we don't need a continuous
    /// browser, just a fresh snapshot when the user clicks the menu item.
    /// </summary>
    public static class BonjourBrowser
    {
        private const string ServiceType = "_prodrive._tcp.local.";
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(4);

        /// <summary>
        /// Scan the local network for Pro Drive editor peers. Returns
        /// matching peers, optionally filtered to a specific user-id so a
        /// shared-network setup doesn't surface other accounts' Macs.
        /// </summary>
        public static async Task<IReadOnlyList<DiscoveredPeer>> DiscoverAsync(
            string? userIdFilter = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IZeroconfHost> hosts;
            try
            {
                hosts = await ZeroconfResolver
                    .ResolveAsync(ServiceType, ScanTimeout, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<DiscoveredPeer>();
            }
            catch
            {
                // Network errors here are common (firewalls, no IPv6, etc.).
                // Treat as "no peers found" rather than bubbling up.
                return Array.Empty<DiscoveredPeer>();
            }

            var peers = new List<DiscoveredPeer>();
            foreach (var host in hosts)
            {
                var service = host.Services.Values.FirstOrDefault();
                if (service == null) continue;
                if (service.Port <= 0) continue;

                // First addresses entry is normally the IPv4 address; fall
                // through to the host's IPv6 if that's all we got.
                var ipAddress = host.IPAddresses.FirstOrDefault();
                if (string.IsNullOrEmpty(ipAddress)) continue;

                // TXT records arrive as a list of dictionaries — flatten.
                var txt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in service.Properties)
                {
                    foreach (var (k, v) in prop)
                    {
                        if (!string.IsNullOrEmpty(k)) txt[k] = v ?? "";
                    }
                }

                if (!string.IsNullOrEmpty(userIdFilter))
                {
                    if (!txt.TryGetValue("userId", out var hostUser)) continue;
                    if (!string.Equals(hostUser, userIdFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                peers.Add(new DiscoveredPeer(
                    DisplayName: host.DisplayName,
                    HostName: host.Id,
                    IPAddress: ipAddress,
                    Port: service.Port,
                    UserId: txt.GetValueOrDefault("userId"),
                    Version: txt.GetValueOrDefault("version")
                ));
            }
            return peers;
        }
    }

    /// <summary>
    /// Snapshot of a discovered peer on the LAN. Returned by
    /// <see cref="BonjourBrowser.DiscoverAsync"/> and consumed by
    /// <see cref="LanSender"/> to address the POST.
    /// </summary>
    public sealed record DiscoveredPeer(
        string DisplayName,
        string HostName,
        string IPAddress,
        int Port,
        string? UserId,
        string? Version);
}
