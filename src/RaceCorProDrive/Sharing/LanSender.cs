using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RaceCorProDrive.Recording;

namespace RaceCorProDrive.Sharing
{
    /// <summary>
    /// HTTP POST a `.rcpdv` bundle to a discovered peer. Streams the body
    /// from disk so we don't load the whole bundle into memory — race
    /// recordings can be multi-GB.
    /// </summary>
    public sealed class LanSender
    {
        private readonly HttpClient _httpClient;
        private readonly string _bearerToken;

        public LanSender(string bearerToken)
        {
            _bearerToken = bearerToken;
            // No automatic redirect. We're talking to a known peer at a
            // raw IP — a redirect would be a foot-gun.
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            _httpClient = new HttpClient(handler)
            {
                // Big bundles + slow LANs justify a generous total budget.
                Timeout = TimeSpan.FromMinutes(30),
            };
        }

        public sealed record SendResult(
            bool Success,
            HttpStatusCode StatusCode,
            string? ErrorMessage,
            string? RemoteSavedPath);

        /// <summary>
        /// Stream the bundle at <paramref name="bundlePath"/> to the peer.
        /// Calls <paramref name="onProgress"/> with bytes-sent updates.
        /// </summary>
        public async Task<SendResult> SendAsync(
            DiscoveredPeer peer,
            string bundlePath,
            IProgress<long>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(bundlePath))
            {
                return new SendResult(false, 0, $"File not found: {bundlePath}", null);
            }

            var header = BundleReader.ReadHeader(bundlePath);
            if (header == null)
            {
                return new SendResult(false, 0, "Not a Pro Drive bundle", null);
            }

            var fileInfo = new FileInfo(bundlePath);
            var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sharedKey = SharedKey.Derive(_bearerToken);
            var signature = SharedKey.Sign(header.BundleId, timestampMs, sharedKey);

            // Stable host string handles raw IPv6 addresses.
            var host = peer.IPAddress.Contains(':')
                ? $"[{peer.IPAddress}]"
                : peer.IPAddress;
            var uri = new Uri($"http://{host}:{peer.Port}/v1/bundles");

            using var fs = new FileStream(
                bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);

            using var content = new ProgressStreamContent(fs, fileInfo.Length, onProgress);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            content.Headers.ContentLength = fileInfo.Length;

            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
            request.Headers.Add("X-Prodrive-Bundle-Id", header.BundleId);
            request.Headers.Add("X-Prodrive-Timestamp", timestampMs.ToString());
            request.Headers.Add("X-Prodrive-Auth", signature);
            request.Headers.Add("X-Prodrive-Sender", $"prodrive-windows/{header.CreatedBy.Version ?? "0"}");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                return new SendResult(false, 0, ex.Message, null);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new SendResult(false, 0, "Cancelled", null);
            }
            catch (TaskCanceledException)
            {
                return new SendResult(false, 0, "Timed out", null);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new SendResult(false, response.StatusCode, $"Peer rejected: {body}", null);
                }
                // Cheap parse — body shape is { received, bundleId, savedAt }.
                var savedPath = TryExtractSavedAt(body);
                return new SendResult(true, response.StatusCode, null, savedPath);
            }
        }

        private static string? TryExtractSavedAt(string body)
        {
            // No need to pull in a JSON dep — extract the savedAt string
            // with a minimal Span search. If the body shape evolves we'll
            // graduate to System.Text.Json.
            const string key = "\"savedAt\":\"";
            var i = body.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length;
            var j = body.IndexOf('"', i);
            if (j < 0) return null;
            return body.Substring(i, j - i);
        }
    }

    /// <summary>
    /// HttpContent wrapper that streams from the underlying stream and
    /// reports byte-count progress. Used so the Send dialog can show a
    /// real progress bar instead of an indefinite spinner.
    /// </summary>
    internal sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly long _length;
        private readonly IProgress<long>? _onProgress;

        public ProgressStreamContent(Stream source, long length, IProgress<long>? onProgress)
        {
            _source = source;
            _length = length;
            _onProgress = onProgress;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var buffer = new byte[64 * 1024];
            long sent = 0;
            int n;
            while ((n = await _source.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                sent += n;
                _onProgress?.Report(sent);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
