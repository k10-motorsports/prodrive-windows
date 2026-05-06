using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Vertical icon-only nav rail. Mirrors the macOS glass rail —
    /// 56pt wide, 36pt button cells, ultra-thin material backdrop,
    /// hover + active states.
    ///
    /// Layout (top → bottom):
    ///   1. Brand logomark (above the glass shell)
    ///   2. Main destinations: Dashboard / DNA / When / Tracks / Cars / Moments
    ///   3. Divider
    ///   4. Tools group: Settings (nav destination) + Launch HUD (action,
    ///      brand-red filled — replaces the old floating FAB)
    ///   5. Divider
    ///   6. Profile (opens Sign Out flyout)
    /// </summary>
    public sealed class LeftRail : UserControl
    {
        public sealed class Destination
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
            public string IconKind { get; init; } = "";
        }

        /// <summary>
        /// Top-of-rail destinations. Settings is intentionally NOT in
        /// this list — it lives in the tools group near the bottom so
        /// it pairs visually with the Launch HUD action button.
        /// </summary>
        public static readonly IReadOnlyList<Destination> Destinations = new[]
        {
            new Destination { Key = "dashboard", Label = "Dashboard", IconKind = LucideIconKind.Dashboard },
            new Destination { Key = "pitwall",   Label = "Pit Wall",  IconKind = LucideIconKind.PitWall },
            new Destination { Key = "dna",       Label = "DNA",       IconKind = LucideIconKind.Dna },
            new Destination { Key = "when",      Label = "When",      IconKind = LucideIconKind.When },
            new Destination { Key = "tracks",    Label = "Tracks",    IconKind = LucideIconKind.Tracks },
            new Destination { Key = "cars",      Label = "Cars",      IconKind = LucideIconKind.Cars },
            new Destination { Key = "moments",   Label = "Moments",   IconKind = LucideIconKind.Moments },
            new Destination { Key = "library",   Label = "Library",   IconKind = LucideIconKind.Library },
        };

        /// <summary>
        /// Settings rail destination key. Routed through the same
        /// DestinationSelected event as the top destinations so host
        /// pages don't need a separate handler — MainWindow.NavigateToPage
        /// already maps "settings" → SettingsPage.
        /// </summary>
        public const string SettingsKey = "settings";

        public event EventHandler<string>? DestinationSelected;

        /// <summary>
        /// Raised when the user taps the Launch HUD button in the tools
        /// group. Host page should spawn the overlay process and
        /// minimize the dashboard window (same flow the old FAB used).
        /// </summary>
        public event EventHandler? LaunchOverlayRequested;

        public static readonly DependencyProperty SelectedKeyProperty = DependencyProperty.Register(
            nameof(SelectedKey), typeof(string), typeof(LeftRail),
            new PropertyMetadata("dashboard", (d, _) => ((LeftRail)d).Refresh()));

        public string SelectedKey
        {
            get => (string)GetValue(SelectedKeyProperty);
            set => SetValue(SelectedKeyProperty, value);
        }

        /// <summary>
        /// Whether to show the Launch HUD action button. Set false when
        /// the overlay binary isn't bundled next to the host (dev runs).
        /// </summary>
        public static readonly DependencyProperty IsLaunchOverlayAvailableProperty = DependencyProperty.Register(
            nameof(IsLaunchOverlayAvailable), typeof(bool), typeof(LeftRail),
            new PropertyMetadata(false, (d, _) => ((LeftRail)d).RefreshLaunchVisibility()));

        public bool IsLaunchOverlayAvailable
        {
            get => (bool)GetValue(IsLaunchOverlayAvailableProperty);
            set => SetValue(IsLaunchOverlayAvailableProperty, value);
        }

        private readonly StackPanel _stack;
        private readonly Dictionary<string, RailButton> _buttons = new();
        private readonly RailButton _settingsButton;
        private readonly RailButton _launchButton;
        private readonly Border _toolsDivider;

        public LeftRail()
        {
            // Width math: rail must be wide enough to fit the button
            // cell + shell margin + stack padding without clipping the
            // active-state pill. 72pt gives the 44pt button 2pt of
            // buffer on each side after the 12pt shell margin and 6pt
            // stack padding on each side.
            Width = 72;

            _stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                Padding = new Thickness(6, 16, 6, 16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            };

            // ── Top destinations ──────────────────────────────────────
            foreach (var dest in Destinations)
            {
                var btn = new RailButton(dest.IconKind, dest.Label);
                btn.Tapped += (_, __) =>
                {
                    SelectedKey = dest.Key;
                    DestinationSelected?.Invoke(this, dest.Key);
                };
                _buttons[dest.Key] = btn;
                _stack.Children.Add(btn);
            }

            // ── Divider before tools group ────────────────────────────
            _stack.Children.Add(MakeHairline());

            // ── Tools group: Settings + Launch HUD ────────────────────
            // Settings is a regular nav destination — same RailButton
            // styling as the top group so it reads as part of the same
            // navigation system.
            _settingsButton = new RailButton(LucideIconKind.Settings, "Settings");
            _settingsButton.Tapped += (_, __) =>
            {
                SelectedKey = SettingsKey;
                DestinationSelected?.Invoke(this, SettingsKey);
            };
            _buttons[SettingsKey] = _settingsButton;
            _stack.Children.Add(_settingsButton);

            // Launch HUD: brand-red filled action button. Replaces the
            // floating FAB that used to live at the bottom-left of the
            // dashboard. Reads as the primary action of this group
            // because of the filled red treatment, but shares the rail
            // cell geometry so it sits flush with the other buttons.
            _launchButton = new RailButton(LucideIconKind.LaunchHud, "Launch HUD")
            {
                IsAlwaysInactive = true,
                Style = RailButton.ButtonStyle.PrimaryAction,
            };
            _launchButton.Tapped += (_, __) => LaunchOverlayRequested?.Invoke(this, EventArgs.Empty);
            _stack.Children.Add(_launchButton);

            // ── Divider before profile ────────────────────────────────
            _toolsDivider = MakeHairline();
            _stack.Children.Add(_toolsDivider);

            // ── Profile (Sign Out flyout) ─────────────────────────────
            var profileBtn = new RailButton(LucideIconKind.Profile, "Profile") { IsAlwaysInactive = true };
            profileBtn.Tapped += (sender, _) =>
            {
                var flyout = BuildProfileFlyout();
                flyout.ShowAt(profileBtn,
                    new FlyoutShowOptions
                    {
                        Placement = FlyoutPlacementMode.RightEdgeAlignedBottom,
                    });
                ProfileRequested?.Invoke(this, EventArgs.Empty);
            };
            _stack.Children.Add(profileBtn);

            // Glass shell. Top margin reduced from 38 → 8pt; the
            // empty 38pt slot above the shell now hosts the brand
            // logomark (placed in the rail UserControl's outer Grid
            // below) instead of being dead space.
            //
            // VerticalAlignment.Top keeps the shell sized to its
            // content (the icon stack) instead of stretching the
            // glass all the way to the window bottom.
            var shell = new Border
            {
                Background = new AcrylicBrush
                {
                    TintColor = Color.FromArgb(0xCC, 0x0A, 0x0A, 0x14),
                    TintOpacity = 0.45,
                    FallbackColor = Color.FromArgb(0xE6, 0x0A, 0x0A, 0x14),
                },
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(18),
                Margin = new Thickness(12, 8, 0, 12),
                VerticalAlignment = VerticalAlignment.Top,
                Child = _stack,
            };

            // Brand logomark sits in the page's top-left empty area
            // — same column as the rail, above the glass shell.
            var logo = new Logomark
            {
                IconTone = Logomark.Tone.Color,
                Height = 24,
                Width = 36,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var root = new Grid
            {
                VerticalAlignment = VerticalAlignment.Top,
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(logo, 0);
            Grid.SetRow(shell, 1);
            root.Children.Add(logo);
            root.Children.Add(shell);

            Content = root;
            Refresh();
            RefreshLaunchVisibility();
        }

        private static Border MakeHairline()
        {
            // Wrapped in a Border so the parent StackPanel can collapse
            // the slot via Visibility = Collapsed without leaving a gap.
            return new Border
            {
                Margin = new Thickness(0, 8, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = 24,
                    Height = 1,
                    Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                },
            };
        }

        public event EventHandler? ProfileRequested;
        /// <summary>
        /// Raised when the user picks "Sign Out" from the profile
        /// flyout. The host page (or App) should clear the auth state
        /// and route back to the login screen.
        /// </summary>
        public event EventHandler? SignOutRequested;

        /// <summary>
        /// Display name shown as the header of the profile flyout
        /// ("Signed in as &lt;UserName&gt;"). Settable from the host
        /// page so we don't need a direct dep on AuthService here.
        /// </summary>
        public string UserName { get; set; } = "User";

        private MenuFlyout BuildProfileFlyout()
        {
            var flyout = new MenuFlyout();
            // Disabled "Signed in as <name>" header — read-only label
            // that anchors the menu without offering an action.
            var header = new MenuFlyoutItem
            {
                Text = $"Signed in as {UserName}",
                IsEnabled = false,
            };
            flyout.Items.Add(header);
            flyout.Items.Add(new MenuFlyoutSeparator());

            // Settings is reachable from the rail itself now (tools
            // group), so the flyout only carries the Sign Out action.
            var signOut = new MenuFlyoutItem { Text = "Sign Out" };
            signOut.Click += (_, __) => SignOutRequested?.Invoke(this, EventArgs.Empty);
            flyout.Items.Add(signOut);
            return flyout;
        }

        private void Refresh()
        {
            foreach (var (key, btn) in _buttons)
            {
                btn.IsActive = key == SelectedKey;
            }
        }

        private void RefreshLaunchVisibility()
        {
            var visible = IsLaunchOverlayAvailable;
            _launchButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            // When the launch button is hidden, the tools group
            // collapses to just Settings — no need for the second
            // divider since Settings sits flush against the profile
            // section already separated by the upper divider.
            _toolsDivider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 44×40 cell that mirrors the SwiftUI <c>RailIconLabel</c> —
        /// rounded square, brand-tinted fill when active, soft hover.
        /// PrimaryAction style swaps in a filled brand-red treatment
        /// for the Launch HUD button (replaces the old floating FAB).
        /// </summary>
        private sealed class RailButton : UserControl
        {
            public enum ButtonStyle { Default, PrimaryAction }

            public bool IsAlwaysInactive { get; init; }
            public ButtonStyle Style { get; init; } = ButtonStyle.Default;

            private readonly LucideIcon _icon;
            private readonly Border _bg;
            private bool _isActive;
            private bool _isHovered;

            public bool IsActive
            {
                get => _isActive;
                set { _isActive = value; UpdateState(); }
            }

            public RailButton(string iconKind, string tooltip)
            {
                _icon = new LucideIcon { Kind = iconKind, Width = 24, Height = 24 };
                _bg = new Border
                {
                    Width = 44,
                    Height = 40,
                    CornerRadius = new CornerRadius(10),
                    Child = _icon,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(8),
                };

                Width = 44;
                Height = 40;
                Content = _bg;
                ToolTipService.SetToolTip(this, tooltip);

                PointerEntered += (_, __) => { _isHovered = true; UpdateState(); };
                PointerExited += (_, __) => { _isHovered = false; UpdateState(); };

                UpdateState();
            }

            private void UpdateState()
            {
                if (Style == ButtonStyle.PrimaryAction)
                {
                    // Brand-red filled treatment, slightly brighter on
                    // hover. Always reads as the primary action of the
                    // tools group; no active/inactive state since this
                    // is an action, not a destination.
                    _bg.Background = _isHovered
                        ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x5B, 0x47))
                        : new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0x39, 0x35));
                    _bg.BorderBrush = new SolidColorBrush(Color.FromArgb(_isHovered ? (byte)0xFF : (byte)0xCC, 0xFF, 0xFF, 0xFF));
                    _bg.BorderThickness = new Thickness(1);
                    _icon.Opacity = 1.0;
                    return;
                }

                var active = !IsAlwaysInactive && _isActive;
                _bg.Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x59, 0xE5, 0x39, 0x35))
                    : (_isHovered
                        ? new SolidColorBrush(Color.FromArgb(0x80, 0x14, 0x14, 0x2A))
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent));
                _bg.BorderBrush = active
                    ? new SolidColorBrush(Color.FromArgb(0xCC, 0xE5, 0x39, 0x35))
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                _bg.BorderThickness = new Thickness(active ? 1 : 0);
                _icon.Opacity = active ? 1.0 : (_isHovered ? 0.95 : 0.7);
            }
        }
    }
}
