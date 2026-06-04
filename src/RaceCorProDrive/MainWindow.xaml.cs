using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Pages;
using RaceCorProDrive.Auth;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Screensaver;
using RaceCorProDrive.Services;
using Windows.Graphics;

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
        private ScreensaverWindow? _screensaver;

        public MainWindow()
        {
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // SetTitleBar treats every child of AppTitleBar as drag region
            // unless the child is a ButtonBase descendant. Our HorizontalTabs
            // / DashboardTabs are UserControls with manual Tapped handlers,
            // so without an explicit passthrough region the system eats
            // the click before the handler fires. Carve out the two host
            // slots as Passthrough so clicks reach the tab buttons.
            TitleBarTabsHost.SizeChanged += (_, __) => RefreshTitleBarPassthrough();
            TitleBarFiltersHost.SizeChanged += (_, __) => RefreshTitleBarPassthrough();
            AppTitleBar.SizeChanged += (_, __) => RefreshTitleBarPassthrough();

            // Wire the app-level rail. Events are routed window-level
            // (not per-page) since the rail is part of the window chrome
            // now — pages don't need to re-subscribe on every load.
            AppRail.DestinationSelected += OnRailDestinationSelected;
            AppRail.SignOutRequested += OnRailSignOutRequested;
            AppRail.LaunchOverlayRequested += OnRailLaunchOverlayRequested;
            AppRail.ScreensaverRequested += OnRailScreensaverRequested;
            OverlayLauncher.Shared.PropertyChanged += OnOverlayStateChanged;
            // Keep the Screensaver button hidden whenever the sim is running.
            IRacingDetector.Shared.PropertyChanged += OnRacingStateChanged;
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
            AppRail.IsScreensaverAvailable = !IRacingDetector.Shared.IsRunning;
            AppRail.Visibility = Visibility.Visible;
            // ContentFrameLoaded handles the initial page navigation.
        }

        public void NavigateToPage(string tag, object? parameter = null)
        {
            Type pageType = tag switch
            {
                "dashboard" => typeof(DashboardPage),
                "pitwall" => typeof(PitWallPage),
                "library" => typeof(LibraryPage),
                "editor" => typeof(EditorPage),
                "races" => typeof(SessionsPage),
                "moments" => typeof(PlaceholderPage),
                "tracks" => typeof(PlaceholderPage),
                "cars" => typeof(PlaceholderPage),
                "dna" => typeof(DnaPage),
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

        private void OnRailScreensaverRequested(object? sender, EventArgs e)
        {
            // Belt-and-suspenders: the rail button is hidden during a session, but
            // never open the photo wall while the sim is running.
            if (IRacingDetector.Shared.IsRunning) return;

            if (_screensaver is not null)
            {
                _screensaver.Activate();
                return;
            }

            var saver = new ScreensaverWindow();
            saver.Closed += (_, __) => _screensaver = null;
            _screensaver = saver;
            saver.Activate();
        }

        // Hide the Screensaver action whenever the sim is running (and conversely
        // re-offer it when iRacing exits). The wall itself self-closes on this same
        // signal — see ScreensaverWindow. Fires on the detector's poll thread, so
        // marshal the rail update onto the UI thread.
        private void OnRacingStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IRacingDetector.IsRunning)) return;
            DispatcherQueue.TryEnqueue(() =>
                AppRail.IsScreensaverAvailable = !IRacingDetector.Shared.IsRunning);
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
            // Layout hasn't run yet for the new content — defer the
            // passthrough recompute so ActualWidth/Height are populated.
            DispatcherQueue.TryEnqueue(RefreshTitleBarPassthrough);
        }

        public void SetTitleBarFilters(UIElement? filters)
        {
            TitleBarFiltersHost.Content = filters;
            DispatcherQueue.TryEnqueue(RefreshTitleBarPassthrough);
        }

        // ── Titlebar passthrough regions ─────────────────────────────
        //
        // Tells the system "these rects are not titlebar — let clicks
        // through to the app's input pipeline." Without this the drag
        // region eats taps on anything that isn't a ButtonBase. Recompute
        // whenever a host's size or content changes.
        private void RefreshTitleBarPassthrough()
        {
            if (this.AppWindow is null) return;

            var rects = new List<RectInt32>();
            AddPassthroughRect(rects, TitleBarTabsHost);
            AddPassthroughRect(rects, TitleBarFiltersHost);

            var ncSource = InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
            ncSource.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
        }

        private static void AddPassthroughRect(List<RectInt32> rects, FrameworkElement element)
        {
            if (element is null || element.XamlRoot is null) return;
            if (element.ActualWidth < 1 || element.ActualHeight < 1) return;

            // Element bounds in DIPs relative to the XAML root.
            var transform = element.TransformToVisual(null);
            var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var scale = element.XamlRoot.RasterizationScale;

            rects.Add(new RectInt32(
                (int)Math.Round(origin.X * scale),
                (int)Math.Round(origin.Y * scale),
                (int)Math.Round(element.ActualWidth * scale),
                (int)Math.Round(element.ActualHeight * scale)));
        }
    }
}
