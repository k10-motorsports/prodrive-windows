using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RaceCorProDrive.Services;

namespace RaceCorProDrive.Recording
{
    /// <summary>
    /// Host-side recording pipeline. Shells a single FFmpeg child process
    /// that captures the desktop (gdigrab), composites the facecam as
    /// picture-in-picture, mixes mic + system-audio (virtual cable), and
    /// encodes to MP4 with NVENC/QSV/AMF/libx264. The existing Electron
    /// renderer path (recorder.js) still owns the live recording surface
    /// for now — this service is the destination for the migration tracked
    /// in <c>docs/recording-migration.md</c>. Nothing in the app calls into
    /// it yet; it's importable and ready to be wired behind a
    /// <c>recordingBackend == "native"</c> opt-in flag.
    ///
    /// Why FFmpeg + DirectShow instead of <c>Windows.Graphics.Capture</c> +
    /// <c>MediaCapture</c>: a single ffmpeg invocation handles capture +
    /// PiP composite + audio mix + hardware encode + muxing. Doing the
    /// same via WinRT means hand-stitching DXGI desktop duplication, a
    /// MediaCapture session for the webcam, WASAPI loopback for system
    /// audio, a MediaComposition for the PiP, and a MediaTranscoder for
    /// the H.264 mux — multiple async lifecycles to synchronize against
    /// each other. The DirectShow names we feed ffmpeg are the same
    /// friendly device labels the Settings page already persists alongside
    /// the WinRT IDs (Phase 1 of the migration), so no separate device
    /// enumeration is needed here.
    /// </summary>
    public sealed class NativeRecordingService
    {
        public static NativeRecordingService Shared { get; } = new();

        public event EventHandler<RecordingState>? StateChanged;

        private readonly object _lock = new();
        private Process? _ffmpeg;
        private string? _currentPath;
        private DateTime _startedUtc;
        private CancellationTokenSource? _stderrCts;

        public RecordingState State { get; private set; } = RecordingState.Idle;
        public string? CurrentFilePath
        {
            get { lock (_lock) return _currentPath; }
        }
        public TimeSpan Elapsed
            => State == RecordingState.Recording ? DateTime.UtcNow - _startedUtc : TimeSpan.Zero;

        private NativeRecordingService() { }

        /// <summary>
        /// Begin recording. Reads device labels + quality preferences from
        /// the live <see cref="OverlaySettingsService"/>. The returned
        /// <see cref="RecordingStartResult"/> includes the output file
        /// path; on failure, <see cref="RecordingStartResult.Error"/> is
        /// populated and <see cref="State"/> stays <c>Idle</c>.
        /// </summary>
        public Task<RecordingStartResult> StartAsync(RecordingOptions? overrideOptions = null)
        {
            lock (_lock)
            {
                if (State != RecordingState.Idle)
                {
                    return Task.FromResult(new RecordingStartResult
                    {
                        Error = "Already recording",
                    });
                }
                State = RecordingState.Starting;
            }
            EmitState();

            return Task.Run(() => StartInternal(overrideOptions));
        }

        private RecordingStartResult StartInternal(RecordingOptions? overrideOptions)
        {
            try
            {
                var settings = OverlaySettingsService.Shared.Load();
                var opts = RecordingOptions.FromSettings(settings, overrideOptions);

                var ffmpegPath = FFmpegLocator.Find();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    return Fail("ffmpeg.exe not found (set in PATH or bundle with installer)");
                }

                var outDir = ResolveOutputDirectory(opts);
                Directory.CreateDirectory(outDir);
                var outPath = Path.Combine(outDir, BuildFilename(opts));

                var args = BuildFfmpegArgs(opts, outPath);
                var psi = new ProcessStartInfo(ffmpegPath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                };

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    return Fail("Failed to spawn ffmpeg process");
                }

                _stderrCts = new CancellationTokenSource();
                StartStderrPump(proc, _stderrCts.Token);

                lock (_lock)
                {
                    _ffmpeg = proc;
                    _currentPath = outPath;
                    _startedUtc = DateTime.UtcNow;
                    State = RecordingState.Recording;
                }
                EmitState();

                return new RecordingStartResult
                {
                    Success = true,
                    Path = outPath,
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Stop the active recording. Sends 'q' on ffmpeg's stdin for a
        /// graceful finalize (writes moov atom, flushes encoder state). If
        /// that doesn't return in <paramref name="gracefulTimeoutMs"/>, the
        /// process is killed — the resulting file may be unplayable but
        /// at least the user isn't stuck.
        /// </summary>
        public Task<RecordingStopResult> StopAsync(int gracefulTimeoutMs = 5000)
        {
            Process? proc;
            string? path;
            lock (_lock)
            {
                if (State != RecordingState.Recording || _ffmpeg == null)
                {
                    return Task.FromResult(new RecordingStopResult { Error = "Not recording" });
                }
                State = RecordingState.Stopping;
                proc = _ffmpeg;
                path = _currentPath;
            }
            EmitState();

            return Task.Run(() =>
            {
                try
                {
                    try
                    {
                        proc.StandardInput.Write('q');
                        proc.StandardInput.Flush();
                    }
                    catch
                    {
                        // stdin already closed — fall through to wait/kill.
                    }

                    if (!proc.WaitForExit(gracefulTimeoutMs))
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                        proc.WaitForExit();
                    }

                    long fileSize = 0;
                    try { if (path != null) fileSize = new FileInfo(path).Length; } catch { /* ok */ }

                    lock (_lock)
                    {
                        _ffmpeg = null;
                        _currentPath = null;
                        State = RecordingState.Idle;
                    }
                    _stderrCts?.Cancel();
                    EmitState();

                    return new RecordingStopResult
                    {
                        Success = true,
                        Path = path,
                        FileSize = fileSize,
                    };
                }
                catch (Exception ex)
                {
                    lock (_lock) { State = RecordingState.Idle; _ffmpeg = null; }
                    EmitState();
                    return new RecordingStopResult { Error = ex.Message };
                }
            });
        }

        private RecordingStartResult Fail(string error)
        {
            lock (_lock) { State = RecordingState.Idle; _ffmpeg = null; _currentPath = null; }
            EmitState();
            return new RecordingStartResult { Error = error };
        }

        private void EmitState()
        {
            try { StateChanged?.Invoke(this, State); } catch { /* never let listeners crash us */ }
        }

        /// <summary>
        /// Async-drain ffmpeg stderr. Without this the OS pipe buffer fills
        /// (default ~64KB on Windows) and ffmpeg blocks on its next write,
        /// causing the recording to freeze partway through. We only log
        /// lines that look like genuine errors to keep noise down.
        /// </summary>
        private static void StartStderrPump(Process proc, CancellationToken ct)
        {
            Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested && !proc.HasExited)
                    {
                        var line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                            || line.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                            || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"[NativeRec] {line}");
                        }
                    }
                }
                catch { /* pump terminating */ }
            }, ct);
        }

        // ── Filename + path helpers (mirror overlay/main.js conventions) ──

        private static string ResolveOutputDirectory(RecordingOptions opts)
        {
            if (!string.IsNullOrEmpty(opts.OutputDirectory))
            {
                return opts.OutputDirectory!;
            }
            // Match the overlay's default — Windows "Videos" known folder.
            return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        private static string BuildFilename(RecordingOptions opts)
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var parts = new List<string>();
            var t = Slug(opts.TrackName); if (!string.IsNullOrEmpty(t)) parts.Add(t);
            var c = Slug(opts.CarName);   if (!string.IsNullOrEmpty(c)) parts.Add(c);
            var tail = string.Join("_", parts);
            var ext = opts.OutputFormat switch
            {
                "mkv" => "mkv",
                "mov" => "mov",
                _ => "mp4",
            };
            return string.IsNullOrEmpty(tail)
                ? $"RaceCor_{ts}.{ext}"
                : $"RaceCor_{ts}_{tail}.{ext}";
        }

        private static string Slug(string? name, int maxLen = 32)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name.Length);
            var lastDash = false;
            foreach (var raw in name.ToLowerInvariant())
            {
                var ok = (raw >= 'a' && raw <= 'z') || (raw >= '0' && raw <= '9');
                if (ok) { sb.Append(raw); lastDash = false; }
                else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
            }
            var s = sb.ToString().TrimEnd('-');
            return s.Length > maxLen ? s.Substring(0, maxLen).TrimEnd('-') : s;
        }

        // ── FFmpeg argument builder ──

        /// <summary>
        /// Build the ffmpeg argv. One invocation does: desktop capture +
        /// optional facecam PiP composite + optional mic + optional system
        /// audio + hardware encode. Each input ends up at a fixed numeric
        /// stream index that the <c>-filter_complex</c> graph references.
        /// </summary>
        internal static string BuildFfmpegArgs(RecordingOptions opts, string outPath)
        {
            var sb = new StringBuilder();
            sb.Append("-y -hide_banner -loglevel warning ");

            // [0] Desktop capture. gdigrab is reliable on every Windows
            // edition; ddagrab (DXGI desktop duplication) is faster but
            // needs an NVENC + ffmpeg-with-cuda build, so it lives behind
            // a future PreferDdagrab option.
            sb.Append($"-f gdigrab -framerate {opts.FrameRate} -draw_mouse 0 -i desktop ");

            int nextInputIndex = 1;
            int? webcamIndex = null;
            int? micIndex = null;
            int? sysIndex = null;

            // [1?] Webcam — only when a label is set. DirectShow takes the
            // friendly name (same string Phase 1 persists into
            // RecordingWebcamLabel). Quoted because most camera names
            // contain spaces.
            if (!string.IsNullOrEmpty(opts.WebcamLabel))
            {
                sb.Append($"-f dshow -i video=\"{Escape(opts.WebcamLabel!)}\" ");
                webcamIndex = nextInputIndex++;
            }

            if (!string.IsNullOrEmpty(opts.MicLabel))
            {
                sb.Append($"-f dshow -i audio=\"{Escape(opts.MicLabel!)}\" ");
                micIndex = nextInputIndex++;
            }

            if (!string.IsNullOrEmpty(opts.SystemAudioLabel))
            {
                sb.Append($"-f dshow -i audio=\"{Escape(opts.SystemAudioLabel!)}\" ");
                sysIndex = nextInputIndex++;
            }

            // ── filter_complex: PiP overlay + audio mix ──
            var filters = new List<string>();
            string videoOut = "0:v";
            if (webcamIndex.HasValue)
            {
                // Scale the facecam to the requested pixel size, then
                // overlay it at the requested corner. main_w / main_h are
                // ffmpeg variables that resolve to the desktop dimensions
                // at runtime — keeps this resolution-independent.
                var (xExpr, yExpr) = PipPosition(opts.FacecamPos, opts.FacecamMargin);
                filters.Add($"[{webcamIndex}:v]scale={opts.FacecamWidth}:{opts.FacecamHeight},setsar=1[cam]");
                filters.Add($"[0:v][cam]overlay={xExpr}:{yExpr}[vout]");
                videoOut = "vout";
            }

            string? audioOut = null;
            if (micIndex.HasValue && sysIndex.HasValue)
            {
                filters.Add($"[{micIndex}:a]volume={opts.MicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)}[m];" +
                            $"[{sysIndex}:a]volume={opts.SystemVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)}[s];" +
                            $"[m][s]amix=inputs=2:normalize=0:dropout_transition=0[aout]");
                audioOut = "aout";
            }
            else if (micIndex.HasValue)
            {
                filters.Add($"[{micIndex}:a]volume={opts.MicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)}[aout]");
                audioOut = "aout";
            }
            else if (sysIndex.HasValue)
            {
                filters.Add($"[{sysIndex}:a]volume={opts.SystemVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)}[aout]");
                audioOut = "aout";
            }

            if (filters.Count > 0)
            {
                sb.Append($"-filter_complex \"{string.Join(";", filters)}\" ");
            }

            // Bracketed labels are only valid for filter-graph outputs.
            // Raw inputs (no PiP) map as 0:v directly.
            if (videoOut == "0:v") sb.Append("-map 0:v ");
            else sb.Append($"-map \"[{videoOut}]\" ");
            if (audioOut != null) sb.Append($"-map \"[{audioOut}]\" ");

            // ── Encoder selection ──
            var encoder = opts.Encoder;
            if (encoder == "auto") encoder = "h264_nvenc"; // probed at install time in a follow-up
            sb.Append(EncoderArgs(encoder, opts));

            sb.Append("-c:a aac -b:a 192k ");
            sb.Append($"\"{outPath}\"");
            return sb.ToString();
        }

        private static (string x, string y) PipPosition(string pos, int margin)
        {
            return pos switch
            {
                "top-left"     => ($"{margin}",                          $"{margin}"),
                "top-right"    => ($"main_w-overlay_w-{margin}",         $"{margin}"),
                "bottom-left"  => ($"{margin}",                          $"main_h-overlay_h-{margin}"),
                _              => ($"main_w-overlay_w-{margin}",         $"main_h-overlay_h-{margin}"),
            };
        }

        private static string EncoderArgs(string encoder, RecordingOptions opts)
        {
            var br = opts.Quality switch
            {
                "low" => "8M",
                "medium" => "16M",
                "high" => "28M",
                "ultra" => "50M",
                _ => "28M",
            };
            return encoder switch
            {
                "h264_nvenc" => $"-c:v h264_nvenc -preset p5 -tune hq -b:v {br} -maxrate {br} -bufsize {br} ",
                "h264_qsv"   => $"-c:v h264_qsv -preset medium -b:v {br} ",
                "h264_amf"   => $"-c:v h264_amf -quality balanced -b:v {br} ",
                _            => $"-c:v libx264 -preset veryfast -crf 20 -b:v {br} ",
            };
        }

        private static string Escape(string s) => s.Replace("\"", "\\\"");
    }

    public enum RecordingState
    {
        Idle,
        Starting,
        Recording,
        Stopping,
    }

    /// <summary>
    /// Snapshot of every setting the recorder needs, decoupled from
    /// <see cref="OverlaySettings"/> so tests can poke at the FFmpeg
    /// arg builder without standing up the full settings file.
    /// </summary>
    public sealed class RecordingOptions
    {
        public string? WebcamLabel { get; set; }
        public string? MicLabel { get; set; }
        public string? SystemAudioLabel { get; set; }
        public double MicVolume { get; set; } = 0.8;
        public double SystemVolume { get; set; } = 1.0;
        public string Quality { get; set; } = "high";
        public string Encoder { get; set; } = "auto";
        public string OutputFormat { get; set; } = "mp4";
        public string? OutputDirectory { get; set; }
        public int FrameRate { get; set; } = 60;
        public int FacecamWidth { get; set; } = 320;
        public int FacecamHeight { get; set; } = 240;
        public int FacecamMargin { get; set; } = 20;
        public string FacecamPos { get; set; } = "bottom-right";
        public string? CarName { get; set; }
        public string? TrackName { get; set; }

        public static RecordingOptions FromSettings(OverlaySettings s, RecordingOptions? overrideOpts)
        {
            // Friendly labels are what dshow consumes; the WinRT id is
            // kept on the settings model for future native-capture work
            // but isn't useful here.
            var o = new RecordingOptions
            {
                WebcamLabel      = NullIfEmpty(s.RecordingWebcamLabel),
                MicLabel         = (s.RecordingMicEnabled ?? true) ? NullIfEmpty(s.RecordingMicLabel) : null,
                SystemAudioLabel = NullIfEmpty(s.RecordingSystemAudioLabel),
                MicVolume        = s.RecordingMicVolume ?? 0.8,
                SystemVolume     = s.RecordingSystemVolume ?? 1.0,
                Quality          = s.RecordingQuality ?? "high",
                Encoder          = s.RecordingEncoder ?? "auto",
                OutputFormat     = s.RecordingOutputFormat ?? "mp4",
            };
            ApplyFacecamSizePreset(o, s.RecordingFacecamSize);
            o.FacecamPos = s.RecordingFacecamPos ?? "bottom-right";

            if (overrideOpts != null)
            {
                if (overrideOpts.WebcamLabel      != null) o.WebcamLabel      = overrideOpts.WebcamLabel;
                if (overrideOpts.MicLabel         != null) o.MicLabel         = overrideOpts.MicLabel;
                if (overrideOpts.SystemAudioLabel != null) o.SystemAudioLabel = overrideOpts.SystemAudioLabel;
                if (overrideOpts.OutputDirectory  != null) o.OutputDirectory  = overrideOpts.OutputDirectory;
                if (overrideOpts.CarName          != null) o.CarName          = overrideOpts.CarName;
                if (overrideOpts.TrackName        != null) o.TrackName        = overrideOpts.TrackName;
            }
            return o;
        }

        private static void ApplyFacecamSizePreset(RecordingOptions o, string? size)
        {
            switch (size)
            {
                case "small":  o.FacecamWidth = 240; o.FacecamHeight = 180; break;
                case "large":  o.FacecamWidth = 480; o.FacecamHeight = 360; break;
                default:       o.FacecamWidth = 320; o.FacecamHeight = 240; break;
            }
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
    }

    public sealed class RecordingStartResult
    {
        public bool Success { get; set; }
        public string? Path { get; set; }
        public string? Error { get; set; }
    }

    public sealed class RecordingStopResult
    {
        public bool Success { get; set; }
        public string? Path { get; set; }
        public long FileSize { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Resolves ffmpeg.exe at runtime. Priority order:
    ///   1. Override via <c>RACECOR_FFMPEG</c> environment variable
    ///      (useful for dev + CI).
    ///   2. <c>ffmpeg.exe</c> sitting next to RaceCorProDrive.exe
    ///      (installer will drop it here in a follow-up PR).
    ///   3. The Electron overlay's bundled ffmpeg-static (next to or
    ///      a known-relative-path from the host). The overlay already
    ///      ships ffmpeg via npm's <c>ffmpeg-static</c> package; we
    ///      reuse it instead of double-bundling until the installer
    ///      gets its own copy.
    ///   4. <c>ffmpeg</c> on <c>PATH</c> via <c>where</c>.
    /// </summary>
    internal static class FFmpegLocator
    {
        public static string? Find()
        {
            var envPath = Environment.GetEnvironmentVariable("RACECOR_FFMPEG");
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;

            try
            {
                var appDir = AppContext.BaseDirectory;
                var bundled = Path.Combine(appDir, "ffmpeg.exe");
                if (File.Exists(bundled)) return bundled;
            }
            catch { /* ignore */ }

            // Overlay's bundled ffmpeg via electron-builder + asarUnpack.
            // OverlayLauncher already knows the overlay install layout —
            // we walk the same paths it does but target the unpacked
            // ffmpeg-static binary.
            var fromOverlay = FindOverlayBundledFfmpeg();
            if (!string.IsNullOrEmpty(fromOverlay)) return fromOverlay;

            try
            {
                using var p = Process.Start(new ProcessStartInfo("where", "ffmpeg")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                });
                if (p == null) return null;
                var line = p.StandardOutput.ReadLine();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(line) && File.Exists(line)) return line;
            }
            catch { /* ignore */ }

            return null;
        }

        private static string? FindOverlayBundledFfmpeg()
        {
            // Production install layout (Inno Setup writes the overlay
            // tree to {AppDir}\Overlay\). Electron-builder + asarUnpack
            // place ffmpeg-static under resources\app.asar.unpacked\.
            const string Relative = @"resources\app.asar.unpacked\node_modules\ffmpeg-static\ffmpeg.exe";

            try
            {
                var primary = Path.Combine(AppContext.BaseDirectory, "Overlay", Relative);
                if (File.Exists(primary)) return primary;
            }
            catch { /* ignore */ }

            // Dev fallback — sibling prodrive-overlay\dist\win-unpacked\.
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int depth = 0; depth < 8 && dir != null; depth++)
                {
                    var candidate = Path.Combine(
                        dir.FullName, "prodrive-overlay", "dist", "win-unpacked", Relative);
                    if (File.Exists(candidate)) return candidate;
                    // Also try ffmpeg-static straight out of node_modules
                    // (dev tree where the overlay hasn't been packed).
                    var dev = Path.Combine(
                        dir.FullName, "prodrive-overlay",
                        "node_modules", "ffmpeg-static", "ffmpeg.exe");
                    if (File.Exists(dev)) return dev;
                    dir = dir.Parent;
                }
            }
            catch { /* ignore */ }

            return null;
        }
    }
}
