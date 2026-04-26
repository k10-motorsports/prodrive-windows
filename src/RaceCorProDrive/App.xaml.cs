using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.DesignSystem;
using RaceCorProDrive.Support;

namespace RaceCorProDrive
{
    public partial class App : Application
    {
        // Dev override via AppSettings key "racecor.dev.baseUrl"; production default otherwise.
        private const string DefaultBaseUrl = "https://prodrive.racecor.io";

        public App()
        {
            // Catch crashes that would otherwise silently exit the process —
            // unpackaged WinUI 3 swallows startup exceptions by default.
            this.UnhandledException += (s, e) => LogCrash("XamlUnhandled", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex) LogCrash("DomainUnhandled", ex);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => LogCrash("UnobservedTask", e.Exception);

            BootTrace("App.InitializeComponent enter");
            this.InitializeComponent();
            BootTrace("App.InitializeComponent done");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            BootTrace("OnLaunched enter");
            try
            {
                BootTrace("new MainWindow()");
                var window = new MainWindow();
                BootTrace("MainWindow created, calling Activate");
                window.Activate();
                BootTrace("Activate returned");
                _ = TokenStore.Instance.LoadOrFetchAsync(GetBaseUrl());
                BootTrace("TokenStore fetch kicked off");
            }
            catch (Exception ex)
            {
                LogCrash("App.OnLaunched", ex);
                BootTrace($"OnLaunched THREW: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private static void BootTrace(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppSettings.LogsDir, "boot.log"),
                    $"[{DateTime.Now:O}] PID={Environment.ProcessId} {msg}{Environment.NewLine}");
            }
            catch { }
        }

        private static string GetBaseUrl()
        {
            var url = AppSettings.Get("racecor.dev.baseUrl");
            return string.IsNullOrWhiteSpace(url) ? DefaultBaseUrl : url!;
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                var path = Path.Combine(AppSettings.LogsDir, "crash.log");
                File.AppendAllText(path,
                    $"[{DateTime.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* logging failures shouldn't make crashes worse */ }
        }
    }
}
