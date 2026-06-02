using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RaceCorProDrive.Services;

namespace RaceCorProDrive.Recording
{
    /// <summary>
    /// Loopback HTTP control surface for <see cref="NativeRecordingService"/>.
    /// Bound to <c>http://127.0.0.1:&lt;port&gt;/</c>; the chosen port is
    /// written to <c>overlay-settings.json</c> as <c>hostControlPort</c>
    /// so the Electron overlay can find us. Same discovery pattern the
    /// rest of the app uses (the settings file is already the shared
    /// channel between the two processes).
    ///
    /// Endpoints (all loopback-only):
    ///   POST /v1/recording/start  → body optional {car, track, outputDirectory}
    ///   POST /v1/recording/stop
    ///   GET  /v1/recording/state
    ///   GET  /v1/health
    ///
    /// Why <see cref="HttpListener"/> instead of Kestrel: BCL-only, zero
    /// nuget overhead, and we already use it for the OAuth callback
    /// (<see cref="Auth.AuthService"/>). The handful of requests/min this
    /// thing sees doesn't need a full ASP.NET host.
    /// </summary>
    public sealed class RecordingControlServer
    {
        public static RecordingControlServer Shared { get; } = new();

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private int _port;

        public int Port => _port;
        public bool IsRunning => _listener?.IsListening ?? false;

        private RecordingControlServer() { }

        /// <summary>
        /// Bind a free loopback port, start serving, persist the port to
        /// settings so the overlay can find us. Safe to call multiple
        /// times — second + subsequent calls are no-ops.
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            _port = PickFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecCtl] listener.Start failed: {ex.Message}");
                return;
            }

            _listener = listener;
            _cts = new CancellationTokenSource();
            PersistPortToSettings(_port);

            _ = Task.Run(() => AcceptLoop(_cts.Token));
            Debug.WriteLine($"[RecCtl] listening on http://127.0.0.1:{_port}/");
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { /* best effort */ }
            try { _listener?.Stop(); } catch { /* best effort */ }
            try { _listener?.Close(); } catch { /* best effort */ }
            _listener = null;
            _cts = null;
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            var listener = _listener;
            if (listener == null) return;

            while (!ct.IsCancellationRequested && listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    // Listener stopped under us — exit cleanly.
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                // Fire and forget; HttpListener can serve concurrent
                // requests safely. Each handler catches its own errors
                // so one bad request never takes the loop down.
                _ = Task.Run(() => HandleRequestAsync(ctx));
            }
        }

        private static async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                // Loopback-only sanity check — if HttpListener somehow
                // accepts a non-loopback request (it shouldn't with the
                // 127.0.0.1 prefix) we still refuse anything that isn't
                // demonstrably local before doing anything stateful.
                var remote = ctx.Request.RemoteEndPoint?.Address;
                if (remote == null || !IPAddress.IsLoopback(remote))
                {
                    await WriteJsonAsync(ctx, 403, new { error = "loopback only" });
                    return;
                }

                var path = ctx.Request.Url?.AbsolutePath ?? "";
                var method = ctx.Request.HttpMethod;

                if (method == "GET" && path == "/v1/health")
                {
                    await WriteJsonAsync(ctx, 200, new { ok = true, port = Shared.Port });
                    return;
                }

                if (method == "GET" && path == "/v1/recording/state")
                {
                    var svc = NativeRecordingService.Shared;
                    await WriteJsonAsync(ctx, 200, new
                    {
                        state = svc.State.ToString().ToLowerInvariant(),
                        recording = svc.State == RecordingState.Recording,
                        path = svc.CurrentFilePath,
                        elapsedMs = (long)svc.Elapsed.TotalMilliseconds,
                    });
                    return;
                }

                if (method == "POST" && path == "/v1/recording/start")
                {
                    var body = await ReadBodyAsync(ctx);
                    var opts = ParseStartBody(body);
                    var result = await NativeRecordingService.Shared.StartAsync(opts);
                    if (result.Success)
                    {
                        await WriteJsonAsync(ctx, 200, new { success = true, path = result.Path });
                    }
                    else
                    {
                        await WriteJsonAsync(ctx, 500, new { success = false, error = result.Error });
                    }
                    return;
                }

                if (method == "POST" && path == "/v1/recording/stop")
                {
                    var result = await NativeRecordingService.Shared.StopAsync();
                    if (result.Success)
                    {
                        await WriteJsonAsync(ctx, 200, new
                        {
                            success = true,
                            path = result.Path,
                            fileSize = result.FileSize,
                        });
                    }
                    else
                    {
                        await WriteJsonAsync(ctx, 500, new { success = false, error = result.Error });
                    }
                    return;
                }

                await WriteJsonAsync(ctx, 404, new { error = "not found" });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecCtl] handler error: {ex.Message}");
                try { await WriteJsonAsync(ctx, 500, new { error = ex.Message }); } catch { /* ok */ }
            }
        }

        private static async Task<string> ReadBodyAsync(HttpListenerContext ctx)
        {
            if (!ctx.Request.HasEntityBody) return "";
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private static RecordingOptions? ParseStartBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var opts = new RecordingOptions();
                if (root.TryGetProperty("car", out var car) && car.ValueKind == JsonValueKind.String)
                    opts.CarName = car.GetString();
                if (root.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.String)
                    opts.TrackName = track.GetString();
                if (root.TryGetProperty("outputDirectory", out var od) && od.ValueKind == JsonValueKind.String)
                    opts.OutputDirectory = od.GetString();
                return opts;
            }
            catch
            {
                return null;
            }
        }

        private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, object body)
        {
            var json = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            // CORS for the overlay (file:// origin). Same-origin policy
            // generally lets file:// → http://127.0.0.1 through, but
            // browsers vary across Chromium versions; sending Access-
            // Control-Allow-Origin: * costs us nothing and keeps the
            // overlay's fetch() honest.
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static int PickFreePort()
        {
            // Try 8890 first (sibling of SimHub's 8889) so power users
            // can hardcode firewall rules / dev tooling; fall back to
            // an OS-assigned port if it's taken.
            try
            {
                using var probe = new TcpListener(IPAddress.Loopback, 8890);
                probe.Start();
                probe.Stop();
                return 8890;
            }
            catch
            {
                using var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }
        }

        private static void PersistPortToSettings(int port)
        {
            try
            {
                var svc = OverlaySettingsService.Shared;
                var s = svc.Load();
                if (s.HostControlPort == port) return;
                s.HostControlPort = port;
                svc.SaveAsync(s).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecCtl] could not persist port to settings: {ex.Message}");
            }
        }
    }
}
