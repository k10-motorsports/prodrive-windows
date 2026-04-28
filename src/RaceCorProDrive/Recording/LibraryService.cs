using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RaceCorProDrive.Recording
{
    /// <summary>
    /// Scans well-known folders for <c>.rcpdv</c> bundles and returns
    /// parsed metadata. Read-only — sharing/receiving live in <c>Sharing/</c>.
    ///
    /// Locations scanned (first hit wins for dedup by bundleId):
    ///   1. <c>%USERPROFILE%\Documents\RaceCor\sessions\</c>
    ///   2. <c>%USERPROFILE%\Videos\</c>          (overlay default)
    ///   3. <c>%USERPROFILE%\Movies\</c>          (cross-platform fallback)
    ///
    /// Headers are cached in-memory keyed by path + (size, mtime).
    /// </summary>
    public sealed class LibraryService
    {
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly string[] _scanRoots;

        public LibraryService()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _scanRoots = new[]
            {
                Path.Combine(home, "Documents", "RaceCor", "sessions"),
                Path.Combine(home, "Videos"),
                Path.Combine(home, "Movies"),
            };
        }

        /// <summary>
        /// Re-scan disk and return entries sorted most-recent-first.
        /// </summary>
        public Task<IReadOnlyList<LibraryEntry>> RefreshAsync()
        {
            return Task.Run<IReadOnlyList<LibraryEntry>>(() =>
            {
                var found = new List<LibraryEntry>();
                var seen = new HashSet<string>();

                foreach (var root in _scanRoots)
                {
                    if (!Directory.Exists(root)) continue;

                    IEnumerable<string> files;
                    try
                    {
                        files = Directory.EnumerateFiles(root, "*.rcpdv",
                            new EnumerationOptions
                            {
                                RecurseSubdirectories = true,
                                IgnoreInaccessible = true,
                            });
                    }
                    catch (UnauthorizedAccessException) { continue; }
                    catch (IOException) { continue; }

                    foreach (var file in files)
                    {
                        FileInfo info;
                        try { info = new FileInfo(file); }
                        catch { continue; }

                        var fingerprint = $"{info.Length}-{info.LastWriteTimeUtc.Ticks}";

                        if (_cache.TryGetValue(file, out var cached) && cached.Fingerprint == fingerprint)
                        {
                            if (seen.Add(cached.Entry.Header.BundleId))
                            {
                                found.Add(cached.Entry);
                            }
                            continue;
                        }

                        var header = SafeReadHeader(file);
                        if (header == null) continue;

                        var entry = new LibraryEntry(file, header, info.Length, info.LastWriteTime);
                        _cache[file] = new CacheEntry(fingerprint, entry);
                        if (seen.Add(header.BundleId))
                        {
                            found.Add(entry);
                        }
                    }
                }

                found.Sort((a, b) => b.Header.CreatedAt.CompareTo(a.Header.CreatedAt));
                return found;
            });
        }

        private static BundleHeader? SafeReadHeader(string path)
        {
            try { return BundleReader.ReadHeader(path); }
            catch { return null; }
        }

        private sealed record CacheEntry(string Fingerprint, LibraryEntry Entry);
    }

    /// <summary>
    /// One row in the library — file path plus everything cheap from the header.
    /// </summary>
    public sealed class LibraryEntry
    {
        public string Path { get; }
        public BundleHeader Header { get; }
        public long FileSize { get; }
        public DateTime FileMtime { get; }

        public LibraryEntry(string path, BundleHeader header, long fileSize, DateTime fileMtime)
        {
            Path = path;
            Header = header;
            FileSize = fileSize;
            FileMtime = fileMtime;
        }

        public string FileName => System.IO.Path.GetFileName(Path);

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Header.Race.Title)) return Header.Race.Title!;
                var t = Header.Race.Track;
                var c = Header.Race.Car;
                if (!string.IsNullOrEmpty(t) && !string.IsNullOrEmpty(c)) return $"{t} — {c}";
                if (!string.IsNullOrEmpty(t)) return t!;
                if (!string.IsNullOrEmpty(c)) return c!;
                return System.IO.Path.GetFileNameWithoutExtension(Path);
            }
        }

        public string DisplayDuration
        {
            get
            {
                var secs = Math.Max(0, Header.Video.Duration);
                var h = (int)(secs / 3600);
                var m = ((int)secs % 3600) / 60;
                var s = (int)secs % 60;
                return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
            }
        }

        public string DisplayFileSize
        {
            get
            {
                var bytes = (double)FileSize;
                if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000:0.0} GB";
                return $"{bytes / 1_000_000:0} MB";
            }
        }

        public string DisplayCreatedAt => Header.CreatedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
    }
}
