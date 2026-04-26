using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace RaceCorProDrive
{
    /// <summary>
    /// Custom entry point so we can log <em>before</em> any WinUI bootstrap
    /// runs. The auto-generated Main wraps Application.Start in a way
    /// that swallows native init failures silently — when the process
    /// just exits with a brief loading spinner and nothing in the
    /// crash log, that's where it died. Disabled via
    /// DISABLE_XAML_GENERATED_MAIN in the csproj.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            BootLog("entry");

            try
            {
                BootLog("ComWrappersSupport.InitializeComWrappers");
                ComWrappersSupport.InitializeComWrappers();

                BootLog("Application.Start");
                Application.Start(p =>
                {
                    BootLog("Application.Start callback");
                    var ctx = new DispatcherQueueSynchronizationContext(
                        DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(ctx);
                    _ = new App();
                    BootLog("App() constructed");
                });

                BootLog("Application.Start returned (shutdown)");
                return 0;
            }
            catch (Exception ex)
            {
                BootLog($"FATAL: {ex.GetType().FullName}: {ex.Message}");
                BootLog(ex.ToString());
                return 1;
            }
        }

        private static void BootLog(string msg)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RaceCorProDrive", "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "boot.log"),
                    $"[{DateTime.Now:O}] PID={Process.GetCurrentProcess().Id} {msg}{Environment.NewLine}");
            }
            catch { /* never throw from logger */ }
        }
    }
}
