using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Pages;
using RaceCorProDrive.Auth;

namespace RaceCorProDrive
{
    public sealed partial class MainWindow : Window
    {
        private readonly AuthService _authService = new();

        public MainWindow()
        {
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        // Pre-auth: LoginPage. Code-behind calls OnSignInComplete() once
        // sign-in succeeds; that flips this frame off and the authed
        // shell on.
        private void OnAuthFrameLoaded(object sender, RoutedEventArgs e)
        {
            if (_authService.IsAuthenticated)
            {
                ShowAuthedShell();
            }
            else
            {
                AuthFrame.Navigate(typeof(LoginPage));
            }
        }

        // Authed shell: navigate to DashboardPage by default. Pages own
        // their own primary nav (LeftRail), matching the Mac/iOS layout.
        private void OnContentFrameLoaded(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
            {
                ContentFrame.Navigate(typeof(DashboardPage));
            }
        }

        /// <summary>
        /// Display name for the signed-in user. Used by the LeftRail's
        /// profile flyout to show "You're signed in as &lt;name&gt;".
        /// </summary>
        public string CurrentUserDisplayName =>
            _authService.CurrentUser?.DiscordDisplayName ?? "User";

        /// <summary>
        /// Sign out and return to the login screen. Invoked from the
        /// LeftRail's profile flyout's "Sign Out" item.
        /// </summary>
        public void SignOut()
        {
            _authService.SignOut();
            ContentFrame.Visibility = Visibility.Collapsed;
            AuthFrame.Visibility = Visibility.Visible;
            AuthFrame.Navigate(typeof(LoginPage));
        }

        public void OnSignInComplete()
        {
            ShowAuthedShell();
        }

        private void ShowAuthedShell()
        {
            AuthFrame.Visibility = Visibility.Collapsed;
            ContentFrame.Visibility = Visibility.Visible;
            // ContentFrameLoaded handles the initial page navigation.
        }

        // Public so LeftRail / future top-level navigators can drive page
        // changes from anywhere in the visual tree.
        public void NavigateToPage(string tag)
        {
            Type pageType = tag switch
            {
                "dashboard" => typeof(DashboardPage),
                "library" => typeof(LibraryPage),
                "races" => typeof(SessionsPage),
                "moments" => typeof(PlaceholderPage),
                "tracks" => typeof(PlaceholderPage),
                "cars" => typeof(PlaceholderPage),
                "dna" => typeof(PlaceholderPage),
                "when" => typeof(PlaceholderPage),
                "safety" => typeof(PlaceholderPage),
                "composure" => typeof(PlaceholderPage),
                "debrief" => typeof(PlaceholderPage),
                "settings" => typeof(SettingsPage),
                _ => typeof(DashboardPage)
            };
            ContentFrame.Navigate(pageType, tag);
        }

    }
}
