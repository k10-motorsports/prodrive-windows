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
    /// hover + active states. Six top-level destinations matching the
    /// web's lucide-react nav items, plus theme + profile entries
    /// pinned at the bottom.
    /// </summary>
    public sealed class LeftRail : UserControl
    {
        public sealed class Destination
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
            public string IconKind { get; init; } = "";
        }

        public static readonly IReadOnlyList<Destination> Destinations = new[]
        {
            new Destination { Key = "dashboard", Label = "Dashboard", IconKind = LucideIconKind.Dashboard },
            new Destination { Key = "dna",       Label = "DNA",       IconKind = LucideIconKind.Dna },
            new Destination { Key = "when",      Label = "When",      IconKind = LucideIconKind.When },
            new Destination { Key = "tracks",    Label = "Tracks",    IconKind = LucideIconKind.Tracks },
            new Destination { Key = "cars",      Label = "Cars",      IconKind = LucideIconKind.Cars },
            new Destination { Key = "moments",   Label = "Moments",   IconKind = LucideIconKind.Moments },
        };

        public event EventHandler<string>? DestinationSelected;

        public static readonly DependencyProperty SelectedKeyProperty = DependencyProperty.Register(
            nameof(SelectedKey), typeof(string), typeof(LeftRail),
            new PropertyMetadata("dashboard", (d, _) => ((LeftRail)d).Refresh()));

        public string SelectedKey
        {
            get => (string)GetValue(SelectedKeyProperty);
            set => SetValue(SelectedKeyProperty, value);
        }

        private readonly StackPanel _stack;
        private readonly Dictionary<string, RailButton> _buttons = new();

        public LeftRail()
        {
            // Width math: rail must be wide enough to fit the button
            // cell + shell margin + stack padding without clipping the
            // active-state pill. Earlier 60pt rail + 48pt button =
            // overflow by 12pt → clipped on the left. 68pt was an
            // exact fit but the right edge still got shaved by
            // sub-pixel anti-aliasing at the parent's clip boundary.
            // 72pt gives the 44pt button 2pt of buffer on each side
            // (72 - 12 shell margin - 6 stack padL - 6 stack padR -
            // 44 button = 4pt slack, distributed by Center alignment).
            Width = 72;

            // Spacing was tight at 4pt — every icon sat right against
            // its neighbor and the rail read as a stamped strip rather
            // than separate destinations. 10pt lets each cell breathe.
            _stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                Padding = new Thickness(6, 16, 6, 16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            };

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

            // Hairline-ish separator before theme + profile. Extra
            // top + bottom margin gives the divider its own breathing
            // room beyond the stack's per-child spacing.
            _stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 24,
                Height = 1,
                Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                Margin = new Thickness(0, 8, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            // Profile sits below the divider. Tapping it opens a
            // MenuFlyout with the user's display name + a Sign Out
            // action. Anchored to the right edge of the rail so the
            // menu floats next to the icon.
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
            // glass all the way to the window bottom — which is
            // what made the rail appear "full height" after I
            // restructured for the logo.
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
            // Uses the same vector data the macOS / iOS / tvOS builds
            // render (no asset dependency). Tone.Color lights up the
            // red diamond + dark inserts.
            var logo = new Logomark
            {
                IconTone = Logomark.Tone.Color,
                Height = 24,
                Width = 36,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            // Two-row Grid, both rows Auto so the rail UserControl
            // shrinks to fit its content. A Star row would let the
            // shell stretch to whatever vertical space the rail's
            // grid column gives it — that's what made the nav glass
            // run the full window height after the logo restructure.
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
        }

        public event EventHandler? ProfileRequested;
        /// <summary>
        /// Raised when the user picks "Sign Out" from the profile
        /// flyout. The host page (or App) should clear the auth state
        /// and route back to the login screen.
        /// </summary>
        public event EventHandler? SignOutRequested;
        /// <summary>
        /// Raised when the user picks "Settings" from the profile
        /// flyout. Host page navigates to the SettingsPage.
        /// </summary>
        public event EventHandler? SettingsRequested;

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

            // Settings — Segoe Fluent gear glyph (E713) keeps the icon
            // in scope without adding a new lucide SVG to assets.
            var settings = new MenuFlyoutItem
            {
                Text = "Settings",
                Icon = new SymbolIcon(Symbol.Setting),
            };
            settings.Click += (_, __) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            flyout.Items.Add(settings);

            flyout.Items.Add(new MenuFlyoutSeparator());

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

        /// <summary>
        /// 36×36 cell that mirrors the SwiftUI <c>RailIconLabel</c> —
        /// rounded square, brand-tinted fill when active, soft hover.
        /// </summary>
        private sealed class RailButton : UserControl
        {
            public bool IsAlwaysInactive { get; init; }

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
                // 24×24 icon inside a 44×40 cell. Cell is wider than
                // tall so the active pill reads as a pill, not a
                // square. Outer Width must match the inner Border or
                // the active fill clips against the parent stack —
                // see the rail Width math in LeftRail's ctor.
                _icon = new LucideIcon { Kind = iconKind, Width = 24, Height = 24 };
                _bg = new Border
                {
                    Width = 44,
                    Height = 40,
                    CornerRadius = new CornerRadius(10),
                    Child = _icon,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _bg.Padding = new Thickness(8);

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
                // Active state was nearly invisible at 0x2E (~18%
                // brand-red alpha) — bumped to 0x59 (~35%) so the
                // selected destination is unambiguous at a glance
                // without overpowering the icon.
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
