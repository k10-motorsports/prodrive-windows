using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace RaceCorProDrive
{
    /// <summary>
    /// Custom entry point so we can log <em>before</em> any WinUI bootstrap
    /// runs, and so we can handle protocol activations (racecor-native://auth)
    /// for OAuth callbacks.
    /// </summary>
    public static class Program
    {
        // Lets App.OnLaunched and AuthService pull the activation URL on
        // first launch (Windows passes it as command-line arg). Cleared
        // after consumption.
        public static string? PendingActivationUri { get; private set; }

        [STAThread]
        public static int Main(string[] args)
        {
            BootLog("entry");

            try
            {
                // Single-instance + protocol activation. If another instance
                // is already running, redirect this activation to it and
                // exit — that instance will handle the OAuth callback and
                // bring its existing window forward.
                var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                var keyInstance = AppInstance.FindOrRegisterForKey("RaceCorProDrive");
                BootLog($"single-instance: IsCurrent={keyInstance.IsCurrent}, kind={activatedArgs?.Kind}");

                if (!keyInstance.IsCurrent)
                {
                    BootLog("redirecting activation to existing instance and exiting");
                    keyInstance.RedirectActivationToAsync(activatedArgs).AsTask().GetAwaiter().GetResult();
                    return 0;
                }

                // We're the primary instance. Listen for redirected
                // activations from future launches (e.g., clicking the OAuth
                // callback URL while we're already running).
                AppInstance.GetCurrent().Activated += OnRedirectedActivation;

                // Capture the *first launch's* URI if this was a protocol
                // activation (rare in practice — usually the user has the
                // app open before signing in, so OAuth comes back via
                // OnRedirectedActivation above).
                CaptureProtocolUri(activatedArgs);

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

        // Called from the primary instance when a *second* launch is
        // redirected here (e.g., browser hands off racecor-native://auth?...).
        private static void OnRedirectedActivation(object? sender, AppActivationArguments e)
        {
            BootLog($"redirected activation: kind={e.Kind}");
            CaptureProtocolUri(e);
            // App.HandleProtocolActivation polls PendingActivationUri on the
            // UI thread; nothing to do here besides setting it.
            App.HandleProtocolActivation();
        }

        private static void CaptureProtocolUri(AppActivationArguments? args)
        {
            if (args?.Kind == ExtendedActivationKind.Protocol &&
                args.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protoArgs)
            {
                PendingActivationUri = protoArgs.Uri.ToString();
                BootLog($"captured activation URI: {PendingActivationUri}");
            }
        }

        public static string? ConsumePendingActivationUri()
        {
            var u = PendingActivationUri;
            PendingActivationUri = null;
            return u;
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
