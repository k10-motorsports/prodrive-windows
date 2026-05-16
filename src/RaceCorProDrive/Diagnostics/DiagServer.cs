using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RaceCorProDrive.Support;

namespace RaceCorProDrive.Diagnostics
{
    /// <summary>
    /// Minimal HTTP server that exposes <see cref="DiagLog"/> contents
    /// and basic system snapshots over the LAN so a developer on
    /// another machine can stream diagnostics in real time. Built on
    /// <see cref="TcpListener"/> instead of <see cref="HttpListener"/>
    /// so we don't need a URL ACL (which would require elevation /
    /// installer changes) to bind a non-loopback prefix.
    ///
    /// Routes (all GET):
    ///   /            → liveness
    ///   /diag/info   → JSON, app + machine + log-cursor info
    ///   /diag/tail   → JSON, ring entries with seq &gt; ?since=N (max=?max=N, default 500)
    ///   /diag/logs   → JSON, list of log files on disk
    ///   /diag/logs/&lt;name&gt; → text/plain, raw log file (path-traversal safe)
    ///   /diag/snapshot → application/zip, diag/boot/crash logs + system-info.json
    ///
    /// Opt-in via AppSettings:
    ///   racecor.diag.enabled  ("true"/"false", default true)
    ///   racecor.diag.port     (int, default 8891)
    ///   racecor.diag.bindAny  ("true"/"false", default true → 0.0.0.0; false → 127.0.0.1)
    /// </summary>
    public sealed class DiagServer
    {
        public static DiagServer Shared { get; } = new();

        public const string SettingKey_Enabled = "racecor.diag.enabled";
        public const string SettingKey_Port = "racecor.diag.port";
        public const string SettingKey_BindAny = "racecor.diag.bindAny";

        public const int DefaultPort = 8891;

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private int _port;
        private string _bindAddress = "";
        private readonly DateTime _startedAt = DateTime.UtcNow;
        private long _requestCount;

        public int Port => _port;
        public string BindAddress => _bindAddress;
        public bool IsRunning => _listener != null;

        private DiagServer() { }

        public void Start()
        {
            if (IsRunning) return;
            if (!AppSettings.GetBool(SettingKey_Enabled, defaultValue: true))
            {
                DiagLog.Info("diag.server", "DiagServer disabled via setting");
                return;
            }

            var portRaw = AppSettings.Get(SettingKey_Port);
            var port = int.TryParse(portRaw, out var p) ? p : DefaultPort;
            var bindAny = AppSettings.GetBool(SettingKey_BindAny, defaultValue: true);
            var addr = bindAny ? IPAddress.Any : IPAddress.Loopback;

            try
            {
                var listener = new TcpListener(addr, port);
                listener.Start();
                _listener = listener;
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _bindAddress = addr.ToString();
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => AcceptLoop(_cts.Token));

                DiagLog.Info("diag.server", "DiagServer listening", new Dictionary<string, object?>
                {
                    ["bind"] = _bindAddress,
                    ["port"] = _port,
                    ["urls"] = GetReachableUrls(_port),
                });
            }
            catch (Exception ex)
            {
                DiagLog.Exception("diag.server", ex, "DiagServer failed to start");
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _cts = null;
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            var listener = _listener;
            if (listener == null) return;

            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { return; }

                _ = Task.Run(() => HandleClient(client));
            }
        }

        private async Task HandleClient(TcpClient client)
        {
            Interlocked.Increment(ref _requestCount);
            try
            {
                using (client)
                {
                    client.NoDelay = true;
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 30000;
                    using var stream = client.GetStream();
                    var req = await ReadRequestAsync(stream);
                    if (req == null)
                    {
                        await WriteStatusOnly(stream, 400, "Bad Request");
                        return;
                    }

                    await Route(stream, req);
                }
            }
            catch (Exception ex)
            {
                DiagLog.Exception("diag.server", ex, "request failed");
            }
        }

        private async Task Route(NetworkStream stream, HttpRequest req)
        {
            // GET only — nothing here mutates state.
            if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJson(stream, 405, new { error = "method not allowed" });
                return;
            }

            var path = req.Path;

            if (path == "/" || path == "/health")
            {
                await WriteJson(stream, 200, new { ok = true, service = "racecor-diag", port = _port });
                return;
            }

            if (path == "/diag/info")
            {
                await WriteJson(stream, 200, BuildInfo());
                return;
            }

            if (path == "/diag/tail")
            {
                var since = ParseLongQuery(req.Query, "since", 0);
                var max = (int)Math.Clamp(ParseLongQuery(req.Query, "max", 500), 1, 5000);
                var entries = DiagLog.ReadSince(since, max);
                await WriteJson(stream, 200, new
                {
                    head = DiagLog.HeadSeq,
                    count = entries.Count,
                    entries = entries.Select(e => e.ToWireObject()).ToArray(),
                });
                return;
            }

            if (path == "/diag/logs")
            {
                await WriteJson(stream, 200, new { files = ListLogFiles() });
                return;
            }

            if (path.StartsWith("/diag/logs/", StringComparison.Ordinal))
            {
                var name = path.Substring("/diag/logs/".Length);
                await ServeLogFile(stream, name);
                return;
            }

            if (path == "/diag/snapshot")
            {
                await ServeSnapshot(stream);
                return;
            }

            await WriteJson(stream, 404, new { error = "not found", path });
        }

        // -- builders ------------------------------------------------------

        private object BuildInfo()
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version?.ToString() ?? "0.0.0.0";
            var info = FileVersionInfo.GetVersionInfo(asm.Location);
            return new
            {
                service = "racecor-diag",
                appVersion = ver,
                productVersion = info.ProductVersion,
                machine = Environment.MachineName,
                user = Environment.UserName,
                os = Environment.OSVersion.VersionString,
                clr = Environment.Version.ToString(),
                pid = Environment.ProcessId,
                bind = _bindAddress,
                port = _port,
                startedAt = _startedAt.ToString("o"),
                uptimeMs = (long)(DateTime.UtcNow - _startedAt).TotalMilliseconds,
                requestCount = Interlocked.Read(ref _requestCount),
                head = DiagLog.HeadSeq,
                currentLog = DiagLog.CurrentFilePath,
                logsDir = AppSettings.LogsDir,
                urls = GetReachableUrls(_port),
            };
        }

        private static List<object> ListLogFiles()
        {
            var dir = AppSettings.LogsDir;
            var list = new List<object>();
            try
            {
                foreach (var path in Directory.GetFiles(dir))
                {
                    var fi = new FileInfo(path);
                    list.Add(new
                    {
                        name = fi.Name,
                        sizeBytes = fi.Length,
                        modifiedUtc = fi.LastWriteTimeUtc.ToString("o"),
                    });
                }
            }
            catch { /* best effort */ }
            return list;
        }

        private static async Task ServeLogFile(NetworkStream stream, string name)
        {
            // Path-traversal safe: only accept simple filenames, then
            // recombine with the LogsDir. No "..", no separators.
            if (string.IsNullOrWhiteSpace(name) ||
                name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 ||
                name.Contains(".."))
            {
                await WriteJson(stream, 400, new { error = "invalid name" });
                return;
            }
            var path = Path.Combine(AppSettings.LogsDir, name);
            if (!File.Exists(path))
            {
                await WriteJson(stream, 404, new { error = "not found", name });
                return;
            }
            try
            {
                // Open with FileShare.ReadWrite so we don't lock out the
                // writer while a remote client is reading the active file.
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "text/plain; charset=utf-8",
                    ["Content-Length"] = fs.Length.ToString(),
                    ["Cache-Control"] = "no-store",
                    ["Connection"] = "close",
                };
                await WriteHeaders(stream, 200, "OK", headers);
                await fs.CopyToAsync(stream);
            }
            catch (Exception ex)
            {
                DiagLog.Exception("diag.server", ex, "log file stream failed");
            }
        }

        private static async Task ServeSnapshot(NetworkStream stream)
        {
            // Build the zip in memory — typical bundle is well under a
            // few MB (jsonl logs + small text files). Easier than
            // chunked streaming and the snapshot is an on-demand call,
            // not a hot path.
            using var ms = new MemoryStream();
            try
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var dir = AppSettings.LogsDir;
                    foreach (var path in Directory.GetFiles(dir))
                    {
                        try
                        {
                            var entry = zip.CreateEntry($"logs/{Path.GetFileName(path)}", CompressionLevel.Optimal);
                            using var es = entry.Open();
                            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);
                            await fs.CopyToAsync(es);
                        }
                        catch { /* skip files we can't read */ }
                    }

                    var sysInfoEntry = zip.CreateEntry("system-info.json", CompressionLevel.Optimal);
                    using (var sw = new StreamWriter(sysInfoEntry.Open(), Encoding.UTF8))
                    {
                        await sw.WriteAsync(JsonSerializer.Serialize(Shared.BuildInfo(),
                            new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLog.Exception("diag.server", ex, "snapshot build failed");
                await WriteJson(stream, 500, new { error = "snapshot failed" });
                return;
            }

            var bytes = ms.ToArray();
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/zip",
                ["Content-Length"] = bytes.Length.ToString(),
                ["Content-Disposition"] = $"attachment; filename=\"racecor-diag-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip\"",
                ["Cache-Control"] = "no-store",
                ["Connection"] = "close",
            };
            await WriteHeaders(stream, 200, "OK", headers);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        // -- HTTP plumbing -------------------------------------------------

        private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream)
        {
            // We only need request-line + headers for GET. Read up to a
            // sane cap of 8 KiB to avoid slow-loris exhaustion.
            var buf = new byte[8192];
            var read = 0;
            var headerEnd = -1;
            while (read < buf.Length)
            {
                var n = await stream.ReadAsync(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
                headerEnd = FindHeaderEnd(buf, read);
                if (headerEnd >= 0) break;
            }
            if (headerEnd < 0) return null;

            var headerText = Encoding.ASCII.GetString(buf, 0, headerEnd);
            var lines = headerText.Split("\r\n");
            if (lines.Length == 0) return null;

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3) return null;

            var method = requestLine[0];
            var target = requestLine[1];
            var qIdx = target.IndexOf('?');
            var path = qIdx < 0 ? target : target.Substring(0, qIdx);
            var query = qIdx < 0 ? "" : target.Substring(qIdx + 1);

            return new HttpRequest { Method = method, Path = path, Query = query };
        }

        private static int FindHeaderEnd(byte[] buf, int len)
        {
            for (var i = 0; i <= len - 4; i++)
            {
                if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n')
                    return i + 4;
            }
            return -1;
        }

        private static long ParseLongQuery(string query, string key, long def)
        {
            if (string.IsNullOrEmpty(query)) return def;
            foreach (var pair in query.Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var k = pair.Substring(0, eq);
                var v = pair.Substring(eq + 1);
                if (k == key && long.TryParse(v, out var parsed)) return parsed;
            }
            return def;
        }

        private static async Task WriteStatusOnly(NetworkStream stream, int status, string reason)
        {
            var headers = new Dictionary<string, string>
            {
                ["Content-Length"] = "0",
                ["Connection"] = "close",
            };
            await WriteHeaders(stream, status, reason, headers);
        }

        private static async Task WriteJson(NetworkStream stream, int status, object body)
        {
            var json = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(json);
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json; charset=utf-8",
                ["Content-Length"] = bytes.Length.ToString(),
                ["Cache-Control"] = "no-store",
                // The Mac watcher script is a plain curl loop, but
                // leaving CORS open costs nothing and lets a browser-
                // based viewer plug in later.
                ["Access-Control-Allow-Origin"] = "*",
                ["Connection"] = "close",
            };
            await WriteHeaders(stream, status, ReasonFor(status), headers);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static async Task WriteHeaders(NetworkStream stream, int status, string reason,
            IDictionary<string, string> headers)
        {
            var sb = new StringBuilder(256);
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            sb.Append("Server: racecor-diag\r\n");
            sb.Append("Date: ").Append(DateTime.UtcNow.ToString("R")).Append("\r\n");
            foreach (var (k, v) in headers)
            {
                sb.Append(k).Append(": ").Append(v).Append("\r\n");
            }
            sb.Append("\r\n");
            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static string ReasonFor(int status) => status switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            _ => "OK",
        };

        // -- discovery helpers --------------------------------------------

        /// <summary>
        /// Best-effort list of URLs a remote client could use to reach us.
        /// Skips loopback and link-local; surfaces both LAN IPv4 and the
        /// Tailscale interface if present.
        /// </summary>
        private static List<string> GetReachableUrls(int port)
        {
            var urls = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ip.Address)) continue;
                        var s = ip.Address.ToString();
                        if (s.StartsWith("169.254.")) continue;
                        urls.Add($"http://{s}:{port}/diag/info");
                    }
                }
            }
            catch { /* best effort */ }
            return urls;
        }

        private sealed class HttpRequest
        {
            public string Method { get; init; } = "";
            public string Path { get; init; } = "";
            public string Query { get; init; } = "";
        }
    }

}
