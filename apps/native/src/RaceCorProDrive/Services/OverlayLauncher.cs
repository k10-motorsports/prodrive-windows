using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Spawns + tracks the Electron HUD process. The HUD ships next to
    /// this app under <c>Overlay\RaceCorOverlay.exe</c>, so there's no
    /// discovery step — <see cref="ResolveBinary"/> returns the
    /// bundled path. If that file is missing (dev runs where the HUD
    /// hasn't been built yet) we surface that as <see cref="IsAvailable"/>
    /// so the UI can hide the launcher button instead of throwing on
    /// click.
    /// </summary>
    public sealed class OverlayLauncher : INotifyPropertyChanged
    {
        public static OverlayLauncher Shared { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Resolved path to the bundled HUD binary, or null if it isn't installed.</summary>
        public string? BinaryPath { get; }
        public bool IsAvailable => BinaryPath != null && File.Exists(BinaryPath);

        private Process? _process;
        public bool IsRunning
        {
            get => _process is { HasExited: false };
        }

        private OverlayLauncher()
        {
            BinaryPath = ResolveBinary();
        }

        /// <summary>
        /// Spawn the HUD if it isn't already running. Returns the live
        /// process (whether freshly started or pre-existing).
        /// </summary>
        public Process? Launch()
        {
            if (!IsAvailable)
            {
                Debug.WriteLine("[OverlayLauncher] HUD binary not bundled — install includes only the host.");
                return null;
            }
            if (IsRunning) return _process;

            var info = new ProcessStartInfo
            {
                FileName = BinaryPath!,
                WorkingDirectory = Path.GetDirectoryName(BinaryPath!),
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            try
            {
                var proc = Process.Start(info);
                if (proc == null) return null;
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, __) =>
                {
                    _process = null;
                    Raise(nameof(IsRunning));
                };
                _process = proc;
                Raise(nameof(IsRunning));
                return proc;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OverlayLauncher] launch failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Ask the HUD to close. Tries a graceful main-window close
        /// first, then escalates to <c>Kill</c> if the process is
        /// still around after a short grace period.
        /// </summary>
        public void Stop()
        {
            if (_process is not { HasExited: false } proc) return;
            try
            {
                if (!proc.CloseMainWindow())
                {
                    proc.Kill(entireProcessTree: true);
                }
                else if (!proc.WaitForExit(2_000))
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OverlayLauncher] stop failed: {ex.Message}");
            }
            finally
            {
                _process = null;
                Raise(nameof(IsRunning));
            }
        }

        // ── Launch-on-login (HKCU\...\Run) ──

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "RaceCorProDrive";

        /// <summary>
        /// Reflects whether the host is registered to start with
        /// Windows. UI binds to this so the toggle reflects state if
        /// the user removes the entry via Task Manager.
        /// </summary>
        public bool IsLaunchOnLoginEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(RunValueName) is string;
            }
        }

        /// <summary>
        /// Toggle the HKCU Run entry. Stores the host's own EXE — when
        /// the user signs in, Windows launches the host; the host then
        /// reads <see cref="OverlaySettings.WinUIAutoLaunchHud"/> and
        /// either spawns the HUD immediately or waits for a manual
        /// click. Two layers of toggle so "open the host on login" and
        /// "spawn the HUD when the host opens" are independent.
        /// </summary>
        public void SetLaunchOnLogin(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)!;
            if (enabled)
            {
                var hostExe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(hostExe)) return;
                key.SetValue(RunValueName, $"\"{hostExe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            Raise(nameof(IsLaunchOnLoginEnabled));
        }

        // ── Helpers ──

        /// <summary>
        /// Resolves the bundled HUD binary. The combined installer
        /// drops both products into one folder:
        ///   %LOCALAPPDATA%\Programs\RaceCor\
        ///     ├── RaceCorProDrive.exe   (this host, AppContext.BaseDirectory)
        ///     └── Overlay\RaceCorOverlay.exe
        /// </summary>
        private static string? ResolveBinary()
        {
            var candidate = Path.Combine(
                AppContext.BaseDirectory,
                "Overlay",
                "RaceCorOverlay.exe");
            return File.Exists(candidate) ? candidate : null;
        }

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
