using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RaceCorProDrive.Support;

namespace RaceCorProDrive.Diagnostics
{
    /// <summary>
    /// Structured diagnostic logger. Each call appends one JSON object to
    /// <c>%LOCALAPPDATA%\RaceCorProDrive\Logs\diag-&lt;utc&gt;-&lt;pid&gt;.jsonl</c>
    /// and pushes the same record into an in-memory ring so the
    /// <see cref="DiagServer"/> /diag/tail endpoint can serve recent
    /// entries to a remote watcher without re-reading the file.
    ///
    /// Callers should never block on a writer; instead the producer side
    /// is a non-blocking queue drained by a single background task. If
    /// the queue overflows we drop oldest — diagnostics that crash the
    /// app are worse than diagnostics we lose.
    ///
    /// Categories follow a dotted convention: <c>"overlay.render"</c>,
    /// <c>"overlay.hook"</c>, <c>"iracing.sdk"</c>, <c>"auth"</c>,
    /// <c>"recording"</c>, <c>"app.lifecycle"</c>, <c>"crash"</c>.
    /// </summary>
    public static class DiagLog
    {
        public const int RingCapacity = 5000;
        public const int QueueCapacity = 8192;
        public const int FilesToRetain = 10;

        private static long _seq;
        private static readonly ConcurrentQueue<Entry> _ring = new();
        private static readonly object _ringTrimLock = new();

        private static readonly BlockingCollection<Entry> _queue = new(QueueCapacity);
        private static readonly CancellationTokenSource _cts = new();
        private static Task? _writerTask;
        private static string? _currentFilePath;
        private static int _started;

        private static readonly JsonSerializerOptions _json = new()
        {
            // Allow non-ASCII without escaping so logs are readable.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string? CurrentFilePath => _currentFilePath;

        /// <summary>
        /// Start the background writer. Idempotent. Safe to call from
        /// any startup path, including before WinUI bootstrap.
        /// </summary>
        public static void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            try
            {
                var dir = AppSettings.LogsDir;
                var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var pid = Environment.ProcessId;
                _currentFilePath = Path.Combine(dir, $"diag-{ts}-pid{pid}.jsonl");
                TrimOldFiles(dir);
                _writerTask = Task.Run(WriterLoop);

                Info("app.lifecycle", "DiagLog started", new Dictionary<string, object?>
                {
                    ["file"] = _currentFilePath,
                    ["pid"] = pid,
                    ["machine"] = Environment.MachineName,
                    ["os"] = Environment.OSVersion.VersionString,
                    ["clr"] = Environment.Version.ToString(),
                });
            }
            catch
            {
                // Never throw out of the logger.
            }
        }

        public static void Info(string category, string message, IReadOnlyDictionary<string, object?>? props = null)
            => Push("info", category, message, props, null);

        public static void Warn(string category, string message, IReadOnlyDictionary<string, object?>? props = null)
            => Push("warn", category, message, props, null);

        public static void Error(string category, string message, IReadOnlyDictionary<string, object?>? props = null)
            => Push("error", category, message, props, null);

        public static void Exception(string category, Exception ex, string? message = null,
            IReadOnlyDictionary<string, object?>? props = null)
            => Push("error", category, message ?? ex.Message, props, ex);

        /// <summary>
        /// Lightweight structured event — same record shape as Info, but
        /// the message slot holds an event name (e.g. "deviceLost",
        /// "overlayLaunched"). Useful when the consumer wants to filter
        /// on a stable identifier rather than free-form prose.
        /// </summary>
        public static void Event(string category, string eventName, IReadOnlyDictionary<string, object?>? props = null)
            => Push("info", category, eventName, props, null);

        /// <summary>
        /// Snapshot of ring entries with sequence number strictly greater
        /// than <paramref name="sinceSeq"/>. Returns at most <paramref name="maxCount"/>.
        /// Read by <see cref="DiagServer"/> to serve /diag/tail.
        /// </summary>
        public static IReadOnlyList<Entry> ReadSince(long sinceSeq, int maxCount)
        {
            if (maxCount <= 0) return Array.Empty<Entry>();
            return _ring
                .Where(e => e.Seq > sinceSeq)
                .OrderBy(e => e.Seq)
                .Take(maxCount)
                .ToArray();
        }

        public static long HeadSeq => Interlocked.Read(ref _seq);

        private static void Push(string level, string category, string message,
            IReadOnlyDictionary<string, object?>? props, Exception? ex)
        {
            try
            {
                var entry = new Entry
                {
                    Seq = Interlocked.Increment(ref _seq),
                    Ts = DateTime.UtcNow,
                    Level = level,
                    Category = category,
                    Message = message ?? string.Empty,
                    Props = props,
                    Exception = ex == null ? null : new ExceptionInfo
                    {
                        Type = ex.GetType().FullName ?? ex.GetType().Name,
                        Message = ex.Message,
                        Stack = ex.ToString(),
                    },
                };

                AddToRing(entry);

                // TryAdd is non-blocking; on overflow we drop the new
                // record rather than back-pressure the caller. The ring
                // still has it for the in-process /diag/tail consumer.
                _queue.TryAdd(entry);
            }
            catch
            {
                // Swallow — logger must never throw.
            }
        }

        private static void AddToRing(Entry e)
        {
            _ring.Enqueue(e);
            if (_ring.Count <= RingCapacity) return;

            // Cheap trim: drain just enough to get back under cap. Lock
            // so concurrent trimmers don't all hammer it.
            lock (_ringTrimLock)
            {
                while (_ring.Count > RingCapacity && _ring.TryDequeue(out _)) { }
            }
        }

        private static async Task WriterLoop()
        {
            var token = _cts.Token;
            var batch = new List<Entry>(64);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Block for the first item, then opportunistically
                    // drain anything else already queued so we coalesce
                    // file writes.
                    if (!_queue.TryTake(out var first, Timeout.Infinite, token)) break;
                    batch.Add(first);
                    while (batch.Count < 64 && _queue.TryTake(out var next)) batch.Add(next);

                    await FlushBatch(batch);
                    batch.Clear();
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // Don't kill the writer on transient IO; back off a bit.
                    await Task.Delay(250, CancellationToken.None);
                    batch.Clear();
                }
            }
        }

        private static async Task FlushBatch(List<Entry> entries)
        {
            var path = _currentFilePath;
            if (path == null) return;

            var sb = new StringBuilder(entries.Count * 192);
            foreach (var e in entries)
            {
                sb.Append(JsonSerializer.Serialize(e.ToWireObject(), _json));
                sb.Append('\n');
            }

            try
            {
                await using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                    bufferSize: 4096, useAsync: true);
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                await fs.WriteAsync(bytes, 0, bytes.Length);
            }
            catch
            {
                // Disk full / file locked / etc — the ring still has the
                // entries so a remote watcher can still pull them.
            }
        }

        private static void TrimOldFiles(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "diag-*.jsonl")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(FilesToRetain)
                    .ToArray();
                foreach (var f in files)
                {
                    try { f.Delete(); } catch { /* best effort */ }
                }
            }
            catch { /* best effort */ }
        }

        public sealed class Entry
        {
            public long Seq { get; init; }
            public DateTime Ts { get; init; }
            public string Level { get; init; } = "info";
            public string Category { get; init; } = "";
            public string Message { get; init; } = "";
            public IReadOnlyDictionary<string, object?>? Props { get; init; }
            public ExceptionInfo? Exception { get; init; }

            /// <summary>
            /// Compact wire shape — short keys, ISO-8601 UTC timestamp,
            /// nullable fields omitted. Both the on-disk JSONL file and
            /// the /diag/tail HTTP response use this exact layout so a
            /// remote consumer sees one format everywhere.
            /// </summary>
            public Dictionary<string, object?> ToWireObject()
            {
                var d = new Dictionary<string, object?>(7)
                {
                    ["seq"] = Seq,
                    ["ts"] = Ts.ToString("o"),
                    ["lvl"] = Level,
                    ["cat"] = Category,
                    ["msg"] = Message,
                };
                if (Props != null && Props.Count > 0) d["props"] = Props;
                if (Exception != null) d["exc"] = new Dictionary<string, object?>
                {
                    ["type"] = Exception.Type,
                    ["message"] = Exception.Message,
                    ["stack"] = Exception.Stack,
                };
                return d;
            }
        }

        public sealed class ExceptionInfo
        {
            public string Type { get; init; } = "";
            public string Message { get; init; } = "";
            public string Stack { get; init; } = "";
        }
    }
}
