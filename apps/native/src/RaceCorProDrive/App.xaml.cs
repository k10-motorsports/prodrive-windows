using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.DesignSystem;
using Windows.Storage;

namespace RaceCorProDrive
{
    public partial class App : Application
    {
        // Dev override via Windows.Storage.ApplicationData.Current.LocalSettings
        // key "racecor.dev.baseUrl"; production default otherwise.
        private const string DefaultBaseUrl = "https://prodrive.racecor.io";

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var window = new MainWindow();
            window.Activate();

            // Kick off design-token fetch fire-and-forget. If it fails we keep
            // compile-time defaults from Tokens.cs. TokensChanged event fires
            // if the fetched tokens differ, letting UI redraw if bound.
            _ = TokenStore.Instance.LoadOrFetchAsync(GetBaseUrl());
        }

        private static string GetBaseUrl()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue("racecor.dev.baseUrl", out var v) && v is string url && !string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
            return DefaultBaseUrl;
        }
    }
}
