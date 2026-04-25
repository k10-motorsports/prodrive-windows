using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using RaceCorProDrive.Api;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Right sidebar with Tracks / Cars tabs and per-row cards. Mirrors
    /// the SwiftUI <c>SidebarTabs</c>: 320pt wide column wrapped in a
    /// glass panel; tab header pinned at top; scrollable list below.
    /// </summary>
    public sealed class SidebarTabs : UserControl
    {
        public static readonly DependencyProperty DashboardProperty = DependencyProperty.Register(
            nameof(Dashboard), typeof(Dashboard), typeof(SidebarTabs),
            new PropertyMetadata(null, (d, _) => ((SidebarTabs)d).Rebuild()));

        public Dashboard? Dashboard
        {
            get => (Dashboard?)GetValue(DashboardProperty);
            set => SetValue(DashboardProperty, value);
        }

        private enum Tab { Tracks, Cars }
        private Tab _selected = Tab.Tracks;

        private readonly StackPanel _tabRow;
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _list;

        public SidebarTabs()
        {
            Width = 320;

            _tabRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Padding = new Thickness(14, 0, 14, 0),
                Height = 44,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _list = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                Padding = new Thickness(10, 14, 10, 20),
            };
            _scroll = new ScrollViewer
            {
                Content = _list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled,
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(_tabRow);
            stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 1,
                Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
            });
            stack.Children.Add(_scroll);
            Grid.SetRow(_scroll, 1);

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
                Margin = new Thickness(0, 8, 12, 10),
                Child = stack,
            };
            Content = shell;

            _tabRow.Children.Add(MakeTabButton("Tracks", Tab.Tracks));
            _tabRow.Children.Add(MakeTabButton("Cars", Tab.Cars));

            Rebuild();
        }

        private Border MakeTabButton(string text, Tab tab)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            var underline = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 2,
                Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0x39, 0x35)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -10),
            };
            var grid = new Grid();
            grid.Children.Add(label);
            grid.Children.Add(underline);
            label.HorizontalAlignment = HorizontalAlignment.Center;

            var border = new Border
            {
                Padding = new Thickness(2, 14, 2, 14),
                Child = grid,
            };
            border.Tapped += (_, __) =>
            {
                _selected = tab;
                Rebuild();
            };
            // Selection styling done inside Rebuild() so we don't fight refresh logic.
            return border;
        }

        private void Rebuild()
        {
            // Update tab visuals.
            for (int i = 0; i < _tabRow.Children.Count; i++)
            {
                var b = (Border)_tabRow.Children[i];
                var grid = (Grid)b.Child;
                var label = (TextBlock)grid.Children[0];
                var underline = (Microsoft.UI.Xaml.Shapes.Rectangle)grid.Children[1];
                var isActive = (i == 0 && _selected == Tab.Tracks)
                            || (i == 1 && _selected == Tab.Cars);
                label.Foreground = isActive
                    ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextDimBrush"];
                underline.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }

            _list.Children.Clear();

            if (_selected == Tab.Tracks)
            {
                var tracks = Dashboard?.TopTracks ?? new List<TrackMasteryEntry>();
                if (tracks.Count == 0)
                {
                    _list.Children.Add(EmptyMessage("No track data yet"));
                    return;
                }
                int rank = 1;
                foreach (var track in tracks)
                {
                    _list.Children.Add(new TrackSidebarCard
                    {
                        Rank = rank++,
                        Track = track,
                        Lookup = Dashboard?.Lookups?.TrackFor(track.TrackName),
                    });
                }
            }
            else
            {
                var cars = Dashboard?.TopCars ?? new List<CarAffinityEntry>();
                if (cars.Count == 0)
                {
                    _list.Children.Add(EmptyMessage("No car data yet"));
                    return;
                }
                int rank = 1;
                foreach (var car in cars)
                {
                    _list.Children.Add(new CarSidebarCard
                    {
                        Rank = rank++,
                        Car = car,
                        Lookup = ResolveCarLookup(car, Dashboard?.Lookups),
                    });
                }
            }
        }

        /// <summary>
        /// Walk the affinity entry's individual cars first (matches
        /// the web's <c>matchingModel = car.cars.find(...)</c>),
        /// fall back to the manufacturer key. Same strategy on every
        /// platform; without this, category brands like F4 / GT3
        /// never resolve their brand logo.
        /// </summary>
        private static CarLookup? ResolveCarLookup(CarAffinityEntry affinity, DashboardLookups? lookups)
        {
            if (lookups == null) return null;
            if (affinity.Cars != null)
            {
                foreach (var entry in affinity.Cars)
                {
                    var match = lookups.CarFor(entry.CarModel);
                    if (match != null) return match;
                }
            }
            return lookups.CarFor(affinity.Manufacturer);
        }

        private static TextBlock EmptyMessage(string text) => new TextBlock
        {
            Text = text,
            FontSize = 11,
            Margin = new Thickness(0, 30, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
        };
    }

    /// <summary>
    /// Track sidebar row. Rank, real track silhouette, name, mastery
    /// progress bar, sessions/best/avg footer.
    /// </summary>
    public sealed class TrackSidebarCard : UserControl
    {
        public int Rank { get; set; }
        public TrackMasteryEntry? Track { get; set; }
        public TrackLookup? Lookup { get; set; }

        public TrackSidebarCard()
        {
            Loaded += (_, __) => Rebuild();
        }

        private void Rebuild()
        {
            if (Track == null) return;
            var tier = TierColor(Track.MasteryTier);

            var rankText = new TextBlock
            {
                Text = Rank.ToString(),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Top,
                Width = 18,
            };

            FrameworkElement silhouette = !string.IsNullOrEmpty(Lookup?.SvgPath)
                ? new TrackSilhouette
                {
                    SvgPath = Lookup!.SvgPath!,
                    StrokeBrush = new SolidColorBrush(tier),
                    Width = 48,
                    Height = 30,
                }
                : new Border
                {
                    Width = 48,
                    Height = 30,
                    BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                };

            var name = new TextBlock
            {
                Text = Track.TrackName,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MaxLines = 2,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            };

            var bar = new Grid { Height = 2 };
            bar.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                Height = 2,
            });
            bar.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(tier),
                Height = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = (System.Math.Min(100.0, Track.MasteryScore) / 100.0) * 220,
            });

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            footer.Children.Add(MakeFoot($"{Track.TotalSessions} races"));
            if (Track.BestPosition is { } best) footer.Children.Add(MakeFoot($"P{best}"));
            footer.Children.Add(MakeFoot(Track.AvgPosition is { } avg
                ? avg.ToString("0.0") + "x"
                : "—"));

            var rightCol = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            rightCol.Children.Add(name);
            rightCol.Children.Add(bar);
            rightCol.Children.Add(footer);

            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 6, Height = 6,
                Fill = new SolidColorBrush(tier),
                VerticalAlignment = VerticalAlignment.Top,
            };

            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(rankText, 0);
            Grid.SetColumn(silhouette, 1);
            Grid.SetColumn(rightCol, 2);
            Grid.SetColumn(dot, 3);
            grid.Children.Add(rankText);
            grid.Children.Add(silhouette);
            grid.Children.Add(rightCol);
            grid.Children.Add(dot);

            Content = new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["BgPanelBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Child = grid,
            };
        }

        private static Color TierColor(string tier) => tier switch
        {
            "diamond" => Color.FromArgb(0xFF, 0x7C, 0xDF, 0xF0),
            "gold"    => Color.FromArgb(0xFF, 0xFF, 0xCF, 0x4A),
            "silver"  => Color.FromArgb(0xFF, 0xD0, 0xD0, 0xDC),
            _         => Color.FromArgb(0xFF, 0xCD, 0x7F, 0x32),
        };

        private static TextBlock MakeFoot(string text) => new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
        };
    }

    /// <summary>
    /// Car sidebar row — rank, brand logo on a brand-color chip, name,
    /// affinity bar, sessions footer + brand-colored leading edge.
    /// </summary>
    public sealed class CarSidebarCard : UserControl
    {
        public int Rank { get; set; }
        public CarAffinityEntry? Car { get; set; }
        public CarLookup? Lookup { get; set; }

        public CarSidebarCard()
        {
            Loaded += (_, __) => Rebuild();
        }

        private void Rebuild()
        {
            if (Car == null) return;
            var brandTint = ParseHexOrFallback(Lookup?.Color, "#E53935");

            var rankText = new TextBlock
            {
                Text = Rank.ToString(),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Top,
                Width = 18,
            };

            FrameworkElement logo = Lookup != null
                ? new Border
                {
                    Width = 48, Height = 30,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(brandTint),
                    Padding = new Thickness(6, 3, 6, 3),
                    Child = new BrandLogo { Lookup = Lookup, GlyphHeight = 24 },
                }
                : new Border
                {
                    Width = 48, Height = 30,
                    BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                };

            var name = new TextBlock
            {
                Text = Car.Manufacturer,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            };

            var bar = new Grid { Height = 2 };
            bar.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                Height = 2,
            });
            bar.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(brandTint),
                Height = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = (System.Math.Min(100.0, Car.AffinityScore) / 100.0) * 220,
            });

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            footer.Children.Add(MakeFoot($"{Car.TotalSessions} races"));
            if (Car.BestPosition is { } best) footer.Children.Add(MakeFoot($"P{best}"));

            var rightCol = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            rightCol.Children.Add(name);
            rightCol.Children.Add(bar);
            rightCol.Children.Add(footer);

            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(rankText, 0);
            Grid.SetColumn(logo, 1);
            Grid.SetColumn(rightCol, 2);
            grid.Children.Add(rankText);
            grid.Children.Add(logo);
            grid.Children.Add(rightCol);

            // Brand-color left-edge stripe — matches the SwiftUI
            // CarSidebarCard's UnevenRoundedRectangle accent.
            var stripe = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 3,
                Fill = new SolidColorBrush(brandTint),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var outer = new Grid();
            outer.Children.Add(new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["BgPanelBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Child = grid,
            });
            outer.Children.Add(stripe);

            Content = outer;
        }

        private static Color ParseHexOrFallback(string? hex, string fallback)
        {
            var v = string.IsNullOrEmpty(hex) ? fallback : hex!;
            var trimmed = v.TrimStart('#');
            if (trimmed.Length == 6)
            {
                byte r = System.Convert.ToByte(trimmed.Substring(0, 2), 16);
                byte g = System.Convert.ToByte(trimmed.Substring(2, 2), 16);
                byte b = System.Convert.ToByte(trimmed.Substring(4, 2), 16);
                return Color.FromArgb(0xFF, r, g, b);
            }
            return Color.FromArgb(0xFF, 0xE5, 0x39, 0x35);
        }

        private static TextBlock MakeFoot(string text) => new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
        };
    }
}
