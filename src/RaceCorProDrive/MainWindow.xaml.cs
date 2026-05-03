using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Pages;
using RaceCorProDrive.Auth;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Services;

namespace RaceCorProDrive
{
    /// <summary>
    /// App shell window. Owns the title-bar drag region, the auth /
    /// content frames, and (post-auth) the floating LeftRail. Pages
    /// render into <see cref="ContentFrame"/>; navigating between them
    /// animates only the frame's content because the rail sits outside
    /// the frame as a sibling element.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly AuthService _authService = new();
        private string _currentPageTag = "dashboard";

        public MainWindow()
        {
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Wire the app-level rail. Events are routed window-level
            // (not per-page) since the rail is part of the window chrome
            // now — pages don't need to re-subscribe on every load.
            AppRail.DestinationSelected += OnRailDestinationSelected;
            AppRail.SignOutRequested += OnRailSignOutRequested;
            AppRail.LaunchOverlayRequested += OnRailLaunchOverlayRequested;
            OverlayLauncher.Shared.PropertyChanged += OnOverlayStateChanged;
        }

        // Pre-auth: LoginPage. Code-behind calls OnSignInComplete() once
        // sign-in succeeds; that flips this frame off and the authed
        // shell on. RestoreSessionAsync re-hydrates a persisted session
        // (PasswordVault tokens, refresh if stale, repopulate user) so
        // the app skips the login round trip on every launch.
        private async void OnAuthFrameLoaded(object sender, RoutedEventArgs e)
        {
            var restored = await _authService.RestoreSessionAsync();
            if (restored)
            {
                ShowAuthedShell();
            }
            else
            {
                AuthFrame.Navigate(typeof(LoginPage));
            }
        }

        private void OnContentFrameLoaded(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
            {
                NavigateToPage("dashboard");
            }
        }

        public string CurrentUserDisplayName =>
            _authService.CurrentUser?.DiscordDisplayName ?? "User";

        public void SignOut()
        {
            _authService.SignOut();
            ContentFrame.Visibility = Visibility.Collapsed;
            AppRail.Visibility = Visibility.Collapsed;
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
            AppRail.UserName = CurrentUserDisplayName;
            AppRail.IsLaunchOverlayAvailable = OverlayLauncher.Shared.IsAvailable;
            AppRail.Visibility = Visibility.Visible;
            // ContentFrameLoaded handles the initial page navigation.
        }

        public void NavigateToPage(string tag, object? parameter = null)
        {
            Type pageType = tag switch
            {
                "dashboard" => typeof(DashboardPage),
                "library" => typeof(LibraryPage),
                "editor" => typeof(EditorPage),
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
            _currentPageTag = tag;
            // Editor is a sub-page of Library — keep the rail's
            // active highlight on the parent destination so the user
            // sees they're still inside the Library section.
            AppRail.SelectedKey = tag == "editor" ? "library" : tag;
            ContentFrame.Navigate(pageType, parameter ?? tag);
        }

        // ── Rail event handlers ─────────────────────────────────────

        private void OnRailDestinationSelected(object? sender, string destinationKey)
        {
            // Tapping the destination we're already on is a no-op so
            // we don't churn the page (re-running its OnLoaded hooks).
            if (destinationKey == _currentPageTag) return;
            NavigateToPage(destinationKey);
        }

        private void OnRailSignOutRequested(object? sender, EventArgs e)
            => SignOut();

        private void OnRailLaunchOverlayRequested(object? sender, EventArgs e)
        {
            OverlayLauncher.Shared.Launch();
        }

        private void OnOverlayStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Window stays visible while the overlay runs (we navigate to
            // Settings instead of minimizing), so there's nothing to
            // restore here. Hook is left in place for future hardware
            // signals (e.g. iRacing exited, suggest re-arming the HUD).
        }

        // ── Title-bar slot pushes (called by pages) ──────────────────
        //
        // Pages own their tabs + filter controls (DashboardTabs,
        // SettingsPage's HorizontalTabs, the Race/License DropDownButtons).
        // They construct + wire those controls and hand them to the
        // window-level title-bar slots so the chrome reads as one
        // continuous strip across the top of the window. The slot
        // hosts are ContentControls — assignment to .Content reparents
        // any UIElement cleanly. Pass null on Unload to clear.
        public void SetTitleBarTabs(UIElement? tabs)
        {
            TitleBarTabsHost.Content = tabs;
        }

        public void SetTitleBarFilters(UIElement? filters)
        {
            TitleBarFiltersHost.Content = filters;
        }
    }
}
