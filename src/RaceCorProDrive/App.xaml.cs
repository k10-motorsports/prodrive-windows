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

            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                var window = new MainWindow();
                window.Activate();
                _ = TokenStore.Instance.LoadOrFetchAsync(GetBaseUrl());
            }
            catch (Exception ex)
            {
                LogCrash("App.OnLaunched", ex);
                throw;
            }
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
