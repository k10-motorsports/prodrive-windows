using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceCorProDrive.Recording
{
    /// <summary>
    /// Reader for the <c>.rcpdv</c> portable bundle format. Mirrors the
    /// byte layout produced by <c>prodrive-overlay/modules/js/bundle-writer.js</c>
    /// and the macOS <c>BundleReader.swift</c>.
    ///
    /// Spec: agents/skills/race-bundle-format/SKILL.md
    /// </summary>
    public static class BundleReader
    {
        /// <summary>
        /// 16-byte UUID identifying our private box. First 12 bytes spell
        /// "PRODRIVETLMY" in ASCII; last 4 bytes are the box format version.
        /// </summary>
        public static readonly byte[] ProdriveTelemetryUuid = new byte[]
        {
            0x50, 0x52, 0x4F, 0x44, 0x52, 0x49, 0x56, 0x45, // PRODRIVE
            0x54, 0x4C, 0x4D, 0x59,                         // TLMY
            0x00, 0x00,                                     // reserved
            0x00, 0x01,                                     // box format version
        };

        /// <summary>
        /// Read just the bundle header without decompressing telemetry.
        /// Cheap — appropriate for a library scanner over many files.
        /// Returns null if the file is not a Pro Drive bundle.
        /// </summary>
        public static BundleHeader? ReadHeader(string path)
        {
            if (!File.Exists(path)) return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var (payloadOffset, _) = FindTelemetryBox(fs);
            if (payloadOffset < 0) return null;

            fs.Seek(payloadOffset, SeekOrigin.Begin);
            Span<byte> lenBuf = stackalloc byte[4];
            if (fs.Read(lenBuf) != 4) return null;
            var headerLen = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);

            var headerBytes = new byte[headerLen];
            int read = fs.Read(headerBytes, 0, (int)headerLen);
            if (read != (int)headerLen) return null;

            try
            {
                return JsonSerializer.Deserialize<BundleHeader>(headerBytes, JsonOpts);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read and decompress the telemetry stream. Use lazily — for hour-long
        /// sessions this can be tens of MB decompressed. Never call from a list scan.
        /// </summary>
        public static byte[]? ReadTelemetry(string path)
        {
            if (!File.Exists(path)) return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var (payloadOffset, boxEnd) = FindTelemetryBox(fs);
            if (payloadOffset < 0) return null;

            fs.Seek(payloadOffset, SeekOrigin.Begin);
            Span<byte> lenBuf = stackalloc byte[4];
            if (fs.Read(lenBuf) != 4) return null;
            var headerLen = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);

            var compressedStart = payloadOffset + 4 + headerLen;
            if (compressedStart >= boxEnd) return null;
            var compressedLength = (int)(boxEnd - compressedStart);

            fs.Seek(compressedStart, SeekOrigin.Begin);
            var compressed = new byte[compressedLength];
            int read = fs.Read(compressed, 0, compressedLength);
            if (read != compressedLength) return null;

            using var src = new MemoryStream(compressed);
            using var gz = new GZipStream(src, CompressionMode.Decompress);
            using var dst = new MemoryStream(capacity: compressedLength * 4);
            gz.CopyTo(dst);
            return dst.ToArray();
        }

        // ── Box walking ──────────────────────────────────────────────

        /// <summary>
        /// Walk top-level boxes. Returns (payloadOffset, boxEnd) where payloadOffset
        /// points at the start of <c>[headerLen][headerJson][gzip(jsonl)]</c>, and
        /// boxEnd is one past the last byte of the uuid box. Returns (-1, -1) if
        /// no Prodrive UUID is found.
        /// </summary>
        private static (long payloadOffset, long boxEnd) FindTelemetryBox(Stream stream)
        {
            long offset = 0;
            long total = stream.Length;
            Span<byte> head = stackalloc byte[8];
            Span<byte> uuid = stackalloc byte[16];
            Span<byte> large = stackalloc byte[8];

            while (offset + 8 <= total)
            {
                stream.Seek(offset, SeekOrigin.Begin);
                int n = stream.Read(head);
                if (n != 8) return (-1, -1);

                var size = BinaryPrimitives.ReadUInt32BigEndian(head[..4]);
                var type = Encoding.ASCII.GetString(head.Slice(4, 4));

                long boxBytes;
                if (size == 0)
                {
                    boxBytes = total - offset;
                }
                else if (size == 1)
                {
                    if (stream.Read(large) != 8) return (-1, -1);
                    boxBytes = (long)BinaryPrimitives.ReadUInt64BigEndian(large);
                }
                else
                {
                    boxBytes = size;
                }

                if (type == "uuid" && boxBytes >= 24)
                {
                    stream.Seek(offset + 8, SeekOrigin.Begin);
                    if (stream.Read(uuid) == 16 && uuid.SequenceEqual(ProdriveTelemetryUuid))
                    {
                        return (offset + 24, offset + boxBytes);
                    }
                }

                offset += boxBytes;
            }
            return (-1, -1);
        }

        // ── JSON options ─────────────────────────────────────────────

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
    }

    /// <summary>
    /// Decoded form of the JSON header inside a <c>.rcpdv</c> uuid box.
    /// Keep every field that round-trips through the editor declared here —
    /// missing fields get silently dropped on re-encode (System.Text.Json
    /// version of the round-trip-strip trap).
    /// </summary>
    public sealed class BundleHeader
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("bundleId")]      public string BundleId { get; set; } = "";
        [JsonPropertyName("createdAt")]     public DateTime CreatedAt { get; set; }
        [JsonPropertyName("startedAt")]     public DateTime? StartedAt { get; set; }
        [JsonPropertyName("createdBy")]     public CreatedByInfo CreatedBy { get; set; } = new();
        [JsonPropertyName("race")]          public RaceInfo Race { get; set; } = new();
        [JsonPropertyName("video")]         public VideoInfo Video { get; set; } = new();
        [JsonPropertyName("telemetry")]     public TelemetryInfo Telemetry { get; set; } = new();
    }

    public sealed class CreatedByInfo
    {
        [JsonPropertyName("app")]      public string App { get; set; } = "";
        [JsonPropertyName("version")]  public string? Version { get; set; }
        [JsonPropertyName("platform")] public string? Platform { get; set; }
    }

    public sealed class RaceInfo
    {
        [JsonPropertyName("title")]       public string? Title { get; set; }
        [JsonPropertyName("track")]       public string? Track { get; set; }
        [JsonPropertyName("car")]         public string? Car { get; set; }
        [JsonPropertyName("series")]      public string? Series { get; set; }
        [JsonPropertyName("sessionType")] public string? SessionType { get; set; }
        [JsonPropertyName("sessionId")]   public string? SessionId { get; set; }
        [JsonPropertyName("gameId")]      public string? GameId { get; set; }
        [JsonPropertyName("isDemo")]      public bool? IsDemo { get; set; }
        [JsonPropertyName("userId")]      public string? UserId { get; set; }
    }

    public sealed class VideoInfo
    {
        [JsonPropertyName("duration")]  public double Duration { get; set; }
        [JsonPropertyName("frameRate")] public double FrameRate { get; set; }
        [JsonPropertyName("width")]     public int Width { get; set; }
        [JsonPropertyName("height")]    public int Height { get; set; }
        [JsonPropertyName("codec")]     public string Codec { get; set; } = "h264";
    }

    public sealed class TelemetryInfo
    {
        [JsonPropertyName("frameCount")]        public int FrameCount { get; set; }
        [JsonPropertyName("frameRate")]         public double FrameRate { get; set; }
        [JsonPropertyName("schema")]            public string Schema { get; set; } = "";
        [JsonPropertyName("compressedBytes")]   public int CompressedBytes { get; set; }
        [JsonPropertyName("uncompressedBytes")] public int UncompressedBytes { get; set; }
        [JsonPropertyName("hasSummary")]        public bool? HasSummary { get; set; }
    }
}
