using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Auth;
using RaceCorProDrive.DesignSystem;
using RaceCorProDrive.Services;
using RaceCorProDrive.Support;

namespace RaceCorProDrive
{
    public partial class App : Application
    {
        // Dev override via AppSettings key "racecor.dev.baseUrl"; production default otherwise.
        private const string DefaultBaseUrl = "https://prodrive.racecor.io";

        // Set when the first Window is activated, so we can route OAuth
        // callbacks (which arrive on a worker thread) back to the UI dispatcher.
        private static MainWindow? _mainWindow;
        private static AuthService? _authService;

        /// <summary>
        /// AppSettings key for the iRacing auto-launch toggle. Default
        /// behavior is opt-in but ON — sim starts → overlay launches +
        /// dashboard hides; sim stops → overlay closes + dashboard
        /// re-appears. The upcoming Settings page will surface this
        /// as a user-facing toggle (settings are migrating from the
        /// web app into the Windows host).
        /// </summary>
        public const string SettingKey_AutoLaunchOverlay = "racecor.iracing.autoLaunchOverlay";

        /// <summary>
        /// Active MainWindow accessor for pages that need to call back
        /// up to the shell (e.g. LeftRail's profile flyout invoking
        /// SignOut). Null before the first <see cref="OnLaunched"/>.
        /// </summary>
        public static MainWindow? MainWindow => _mainWindow;

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
                _mainWindow = new MainWindow();
                _authService = new AuthService();
                BootTrace("MainWindow created, calling Activate");
                _mainWindow.Activate();
                BootTrace("Activate returned");
                _ = RaceCorProDrive.DesignSystem.TokenStore.Instance.LoadOrFetchAsync(GetBaseUrl());
                BootTrace("TokenStore fetch kicked off");

                // Start the iRacing detector + bind it to the overlay
                // launcher. When the sim transitions running → stopped
                // we auto-launch / auto-close the overlay (and hide /
                // restore the dashboard window symmetrically with the
                // manual FAB flow). Toggle gated on the per-user
                // setting so a future Settings page can disable it.
                IRacingDetector.Shared.PropertyChanged += OnIRacingStateChanged;
                IRacingDetector.Shared.Start();
                BootTrace("IRacingDetector started");

                // Process any URI captured during initial launch (rare; usually
                // OAuth comes back via redirected activation while we're already
                // running — see HandleProtocolActivation).
                var pending = Program.ConsumePendingActivationUri();
                if (!string.IsNullOrEmpty(pending))
                {
                    BootTrace($"OnLaunched: processing initial activation URI {pending}");
                    _ = ProcessAuthCallbackAsync(pending);
                }
            }
            catch (Exception ex)
            {
                LogCrash("App.OnLaunched", ex);
                BootTrace($"OnLaunched THREW: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Called by Program.cs when a *redirected* activation arrives —
        /// browser handed us racecor-native://auth?... while we were already
        /// running. Marshals back to the UI thread because activation
        /// callbacks fire on a worker thread.
        /// </summary>
        /// <summary>
        /// Called by LoginPage after the loopback OAuth flow completes
        /// successfully. Marshals to the UI thread to flip MainWindow
        /// from the AuthFrame (LoginPage) to the NavView (Dashboard).
        /// </summary>
        public static void NotifySignInComplete()
        {
            BootTrace("NotifySignInComplete");
            var dispatcher = _mainWindow?.DispatcherQueue;
            if (dispatcher == null) return;
            dispatcher.TryEnqueue(() => _mainWindow?.OnSignInComplete());
        }

        public static void HandleProtocolActivation()
        {
            var uri = Program.ConsumePendingActivationUri();
            if (string.IsNullOrEmpty(uri)) return;
            BootTrace($"HandleProtocolActivation: {uri}");

            var dispatcher = _mainWindow?.DispatcherQueue;
            if (dispatcher == null)
            {
                BootTrace("HandleProtocolActivation: no dispatcher yet, dropping");
                return;
            }
            dispatcher.TryEnqueue(() => _ = ProcessAuthCallbackAsync(uri));
        }

        private static async System.Threading.Tasks.Task ProcessAuthCallbackAsync(string uri)
        {
            BootTrace($"ProcessAuthCallback: {uri}");
            try
            {
                if (_authService == null) _authService = new AuthService();
                await _authService.OnAuthCallbackAsync(uri);
                BootTrace("ProcessAuthCallback: auth succeeded");

                _mainWindow?.OnSignInComplete();
            }
            catch (Exception ex)
            {
                LogCrash("ProcessAuthCallback", ex);
                BootTrace($"ProcessAuthCallback THREW: {ex.Message}");
            }
        }

        /// <summary>
        /// Mirrors the manual FAB flow: when iRacing starts, launch
        /// the overlay + hide the dashboard window; when it stops,
        /// close the overlay (the existing OverlayLauncher PropChanged
        /// handler in DashboardPage re-shows the dashboard window).
        /// Gated on the per-user opt-in setting (default ON).
        /// </summary>
        private static void OnIRacingStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IRacingDetector.IsRunning)) return;

            // Re-read the setting on every state change so toggles in
            // the Settings page take effect immediately, no app
            // restart required.
            if (!AppSettings.GetBool(SettingKey_AutoLaunchOverlay, defaultValue: true)) return;

            var dispatcher = _mainWindow?.DispatcherQueue;
            if (dispatcher == null) return;

            if (IRacingDetector.Shared.IsRunning)
            {
                // Sim started - launch overlay + minimize dashboard.
                // Minimize (not Hide) because AppWindow.Hide on the
                // chromeless host's only top-level Window can collapse
                // the dispatcher loop and shut the process down. The
                // DashboardPage OnOverlayStateChanged handler restores
                // the window when the overlay exits.
                dispatcher.TryEnqueue(() =>
                {
                    if (OverlayLauncher.Shared.IsRunning) return;
                    var proc = OverlayLauncher.Shared.Launch();
                    if (proc != null)
                    {
                        var presenter = _mainWindow?.AppWindow?.Presenter
                            as Microsoft.UI.Windowing.OverlappedPresenter;
                        presenter?.Minimize();
                    }
                });
            }
            else
            {
                // Sim stopped — close overlay. OverlayLauncher fires
                // its own PropertyChanged on Process.Exited, which the
                // DashboardPage handler then uses to re-show the
                // dashboard window. Symmetric with the manual close
                // path so we don't need duplicate restore logic here.
                dispatcher.TryEnqueue(() =>
                {
                    if (OverlayLauncher.Shared.IsRunning)
                    {
                        OverlayLauncher.Shared.Stop();
                    }
                });
            }
        }

        private static void BootTrace(string msg)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RaceCorProDrive", "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "boot.log"),
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
