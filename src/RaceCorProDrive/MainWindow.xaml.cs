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

        // Fires when the pre-auth frame mounts. If unauth → load LoginPage there.
        // If already authed → flip to NavView (which will then trigger OnNavViewLoaded).
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

        // Called from LoginPage once auth completes (TODO: wire LoginPage to call this).
        public void OnSignInComplete()
        {
            ShowAuthedShell();
        }

        private void ShowAuthedShell()
        {
            AuthFrame.Visibility = Visibility.Collapsed;
            NavView.Visibility = Visibility.Visible;
        }

        private void OnNavViewLoaded(object sender, RoutedEventArgs e)
        {
            UpdateUserDisplay();
            NavView.SelectedItem = NavDashboard;
            // TEMP: Land on PlaceholderPage instead of DashboardPage to
            // isolate the post-sign-in stowed-exception crash. Dashboard
            // page or one of its controls (LeftRail/Sidebar/etc.) is
            // throwing in native XAML code. Swap back once root cause found.
            ContentFrame.Navigate(typeof(PlaceholderPage), "dashboard");
            NavView.SelectionChanged += OnNavViewSelectionChanged;
        }

        private void OnNavViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item) return;
            var tag = item.Tag?.ToString() ?? "dashboard";
            NavigateToPage(tag);
        }

        private void NavigateToPage(string tag)
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

        private void OnSignOut(object sender, RoutedEventArgs e)
        {
            _authService.SignOut();
            NavView.Visibility = Visibility.Collapsed;
            AuthFrame.Visibility = Visibility.Visible;
            AuthFrame.Navigate(typeof(LoginPage));
            NavView.SelectionChanged -= OnNavViewSelectionChanged;
        }

        private void UpdateUserDisplay()
        {
            if (_authService.CurrentUser != null)
            {
                UserDisplay.Text = _authService.CurrentUser.DiscordDisplayName ?? "User";
            }
        }
    }
}
