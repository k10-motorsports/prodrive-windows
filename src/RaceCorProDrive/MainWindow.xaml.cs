using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

        private void OnNavViewLoaded(object sender, RoutedEventArgs e)
        {
            // Check if user is authenticated
            if (!_authService.IsAuthenticated)
            {
                ContentFrame.Navigate(typeof(LoginPage));
                return;
            }

            UpdateUserDisplay();
            NavView.SelectedItem = NavDashboard;
            ContentFrame.Navigate(typeof(DashboardPage));
            NavView.SelectionChanged += OnNavViewSelectionChanged;
        }

        private void OnNavViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item)
                return;

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
                "settings" => typeof(PlaceholderPage),
                _ => typeof(DashboardPage)
            };

            ContentFrame.Navigate(pageType, tag);
        }

        private void OnSignOut(object sender, RoutedEventArgs e)
        {
            _authService.SignOut();
            ContentFrame.Navigate(typeof(LoginPage));
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
