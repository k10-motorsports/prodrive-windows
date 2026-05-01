using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Detects whether iRacing is running by probing the iRacing SDK's
    /// shared memory-mapped file (<c>Local\IRSDKMemMapFileName</c>).
    /// The file's existence is the canonical "sim is active" signal —
    /// iRacing creates it on launch and tears it down on exit, so
    /// open-existing succeeds iff the sim is running.
    /// </summary>
    /// <remarks>
    /// 5-second polling cadence balances responsiveness (overlay
    /// launches within ~5s of iRacing starting) against background
    /// CPU (probe is a kernel call into the global section namespace,
    /// effectively free). Single shared instance so the rest of the
    /// app subscribes to one source of truth.
    /// </remarks>
    public sealed class IRacingDetector : INotifyPropertyChanged
    {
        public static IRacingDetector Shared { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public const int PollIntervalSeconds = 5;
        private const string MmfName = "Local\\IRSDKMemMapFileName";

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                Raise();
            }
        }

        private CancellationTokenSource? _cts;

        /// <summary>
        /// Starts (or restarts) the polling loop. Safe to call
        /// repeatedly — any existing loop is cancelled first. Pair
        /// with <see cref="Stop"/> on app shutdown if needed; we
        /// otherwise leave it running for the lifetime of the
        /// process so cross-page handlers stay armed.
        /// </summary>
        public void Start()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var running = ProbeMmf();
                    if (running != _isRunning)
                    {
                        Debug.WriteLine($"[IRacingDetector] sim state changed → {(running ? "running" : "stopped")}");
                    }
                    IsRunning = running;
                    try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), token); }
                    catch (TaskCanceledException) { return; }
                }
            }, token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        /// <summary>
        /// Tries to open the iRacing SDK MMF. Returns true if the
        /// section exists (sim is running), false otherwise. We
        /// catch broadly because the actual exception type varies
        /// (FileNotFoundException is typical, but UAC + global
        /// section permissions can surface other access errors that
        /// are still effectively "not running" from our perspective).
        /// </summary>
        private static bool ProbeMmf()
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(
                    MmfName,
                    MemoryMappedFileRights.Read);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
