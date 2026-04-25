using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

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
            Width = 56;

            _stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Padding = new Thickness(6, 10, 6, 10),
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

            // Hairline-ish separator before theme + profile.
            _stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 20,
                Height = 1,
                Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            // Theme + profile use the same RailButton chrome but
            // never show the active pill. Hooked up by the page so
            // they can open menus or settings without LeftRail
            // owning that surface area.
            var themeBtn = new RailButton("paintpalette", "Theme") { IsAlwaysInactive = true };
            ThemeRequested += (_, __) => { };
            themeBtn.Tapped += (_, __) => ThemeRequested?.Invoke(this, EventArgs.Empty);
            _stack.Children.Add(themeBtn);

            var profileBtn = new RailButton(LucideIconKind.Profile, "Profile") { IsAlwaysInactive = true };
            profileBtn.Tapped += (_, __) => ProfileRequested?.Invoke(this, EventArgs.Empty);
            _stack.Children.Add(profileBtn);

            // Glass shell.
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
                Margin = new Thickness(12, 38, 0, 12),
                VerticalAlignment = VerticalAlignment.Top,
                Child = _stack,
            };

            Content = shell;
            Refresh();
        }

        public event EventHandler? ThemeRequested;
        public event EventHandler? ProfileRequested;

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
        private sealed class RailButton : Border
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
                _icon = new LucideIcon { Kind = iconKind, Width = 22, Height = 22 };
                _bg = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(9),
                    Child = _icon,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _bg.Padding = new Thickness(7);

                Width = 44;
                Height = 36;
                Child = _bg;
                ToolTipService.SetToolTip(this, tooltip);

                PointerEntered += (_, __) => { _isHovered = true; UpdateState(); };
                PointerExited += (_, __) => { _isHovered = false; UpdateState(); };

                UpdateState();
            }

            private void UpdateState()
            {
                var active = !IsAlwaysInactive && _isActive;
                _bg.Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x2E, 0xE5, 0x39, 0x35))
                    : (_isHovered
                        ? new SolidColorBrush(Color.FromArgb(0x80, 0x14, 0x14, 0x2A))
                        : new SolidColorBrush(Colors.Transparent));
                _bg.BorderBrush = active
                    ? (Brush)Application.Current.Resources["BorderAccentBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                _bg.BorderThickness = new Thickness(active ? 1 : 0);
                _icon.Opacity = active || _isHovered ? 1.0 : 0.65;
            }
        }
    }
}
