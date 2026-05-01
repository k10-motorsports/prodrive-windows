using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using RaceCorProDrive.Api;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Next Race tab — ranked race-suggestion ideas grouped into a
    /// 2-column layout by category (sports car / formula / oval / dirt).
    /// Mirrors the SwiftUI <c>NextRaceTabContent</c>: column header is
    /// a colored bullet + label + bottom hairline; cards stack
    /// vertically within each column. All copy + colors come from the
    /// server's <c>viewModel</c>; client falls back to local formatting
    /// only against legacy server responses.
    /// </summary>
    public sealed class NextRaceTabContent : UserControl
    {
        public static readonly DependencyProperty IdeasProperty = DependencyProperty.Register(
            nameof(Ideas), typeof(IReadOnlyList<NextRaceIdeaEntry>), typeof(NextRaceTabContent),
            new PropertyMetadata(null, (d, _) => ((NextRaceTabContent)d).Rebuild()));

        public static readonly DependencyProperty LookupsProperty = DependencyProperty.Register(
            nameof(Lookups), typeof(DashboardLookups), typeof(NextRaceTabContent),
            new PropertyMetadata(null, (d, _) => ((NextRaceTabContent)d).Rebuild()));

        public IReadOnlyList<NextRaceIdeaEntry>? Ideas
        {
            get => (IReadOnlyList<NextRaceIdeaEntry>?)GetValue(IdeasProperty);
            set => SetValue(IdeasProperty, value);
        }

        public DashboardLookups? Lookups
        {
            get => (DashboardLookups?)GetValue(LookupsProperty);
            set => SetValue(LookupsProperty, value);
        }

        // Display order matches the web (`CAT_ORDER`).
        private static readonly string[] CategoryOrder =
            { "road", "formula", "oval", "dirt_road", "dirt_oval" };

        private static readonly Dictionary<string, string> CategoryColor = new()
        {
            ["road"]      = "#e53935",
            ["formula"]   = "#00bcd4",
            ["oval"]      = "#1e88e5",
            ["dirt_road"] = "#43a047",
            ["dirt_oval"] = "#ff9800",
        };

        private static readonly Dictionary<string, string> CategoryLabel = new()
        {
            ["road"]      = "Sports Car",
            ["formula"]   = "Formula",
            ["oval"]      = "Oval",
            ["dirt_road"] = "Dirt Road",
            ["dirt_oval"] = "Dirt Oval",
        };

        public NextRaceTabContent()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            var ideas = Ideas;
            if (ideas == null || ideas.Count == 0)
            {
                Content = BuildEmptyState();
                return;
            }

            var byCategory = new Dictionary<string, List<NextRaceIdeaEntry>>();
            foreach (var idea in ideas)
            {
                var key = string.IsNullOrEmpty(idea.Category) ? "road" : idea.Category;
                if (!byCategory.TryGetValue(key, out var list))
                {
                    list = new List<NextRaceIdeaEntry>();
                    byCategory[key] = list;
                }
                list.Add(idea);
            }

            // Two-column grid mirroring the web's md:grid-cols-2.
            var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int col = 0, row = 0;
            foreach (var cat in CategoryOrder)
            {
                if (!byCategory.TryGetValue(cat, out var items) || items.Count == 0) continue;

                var column = BuildCategoryColumn(
                    title: CategoryLabel.GetValueOrDefault(cat, cat),
                    accentHex: CategoryColor.GetValueOrDefault(cat, "#888888"),
                    items: items);
                Grid.SetColumn(column, col);
                Grid.SetRow(column, row);
                if (row >= grid.RowDefinitions.Count)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                grid.Children.Add(column);

                col++;
                if (col >= 2)
                {
                    col = 0;
                    row++;
                }
            }

            Content = grid;
        }

        private FrameworkElement BuildCategoryColumn(string title, string accentHex, List<NextRaceIdeaEntry> items)
        {
            var accent = ParseHex(accentHex);
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };

            // Header — bullet + label + bottom border.
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bullet = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(bullet, 0);
            var label = new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 200,
                Foreground = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 1);
            headerRow.Children.Add(bullet);
            headerRow.Children.Add(label);

            var headerWrap = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            headerWrap.Children.Add(headerRow);
            headerWrap.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(accent),
            });
            stack.Children.Add(headerWrap);

            foreach (var idea in items)
            {
                var trackLookup = Lookups?.TrackFor(idea.TrackName);
                var carLookups = idea.CarClassNames
                    .Select(n => Lookups?.CarFor(n))
                    .Where(c => c != null)
                    .Cast<CarLookup>()
                    .ToList();
                stack.Children.Add(new NextRaceCard
                {
                    Idea = idea,
                    TrackLookup = trackLookup,
                    CarLookups = carLookups,
                });
            }

            return stack;
        }

        private static FrameworkElement BuildEmptyState()
        {
            var inner = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 420,
            };
            inner.Children.Add(new TextBlock
            {
                Text = "No race suggestions yet",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            inner.Children.Add(new TextBlock
            {
                Text = "Run a few more sessions — the Next Race engine needs 3+ races to compute suggestions.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return new Panel
            {
                Padding = new Thickness(40),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = inner,
            };
        }

        internal static Color ParseHex(string? hex, string fallback = "#888888")
        {
            var v = string.IsNullOrEmpty(hex) ? fallback : hex!;
            var t = v.TrimStart('#');
            if (t.Length == 6)
            {
                byte r = Convert.ToByte(t.Substring(0, 2), 16);
                byte g = Convert.ToByte(t.Substring(2, 2), 16);
                byte b = Convert.ToByte(t.Substring(4, 2), 16);
                return Color.FromArgb(0xFF, r, g, b);
            }
            return Color.FromArgb(0xFF, 0x88, 0x88, 0x88);
        }
    }

    /// <summary>
    /// Hero-style "Next Race" card. Mirrors the SwiftUI
    /// <c>NextRaceCard</c>: 160-tall hero header with track image +
    /// silhouette overlay + time/score chips + brand chips, then a
    /// body with track name + series + license/official/fixed/duration
    /// tags + strategy pill + commentary. Server's pre-rendered
    /// <c>viewModel</c> drives every label and color; local fallbacks
    /// only fire when the server hasn't shipped a viewModel yet.
    /// </summary>
    public sealed class NextRaceCard : UserControl
    {
        public NextRaceIdeaEntry? Idea { get; set; }
        public TrackLookup? TrackLookup { get; set; }
        public List<CarLookup> CarLookups { get; set; } = new();

        public NextRaceCard()
        {
            Loaded += (_, __) => Rebuild();
        }

        private Color ScoreTint =>
            !string.IsNullOrEmpty(Idea?.ViewModel?.ScoreColorHex)
                ? NextRaceTabContent.ParseHex(Idea!.ViewModel!.ScoreColorHex)
                : (Idea?.Score ?? 0) switch
                {
                    >= 80 => NextRaceTabContent.ParseHex("#34C759"),
                    >= 60 => NextRaceTabContent.ParseHex("#7CB342"),
                    >= 40 => NextRaceTabContent.ParseHex("#FFB300"),
                    _     => NextRaceTabContent.ParseHex("#FF7043"),
                };

        private Color StrategyColor =>
            !string.IsNullOrEmpty(Idea?.ViewModel?.StrategyColorHex)
                ? NextRaceTabContent.ParseHex(Idea!.ViewModel!.StrategyColorHex)
                : (Idea?.Strategy?.ToLowerInvariant()) switch
                {
                    "pitlane"      => NextRaceTabContent.ParseHex("#E53935"),
                    "conservative" => NextRaceTabContent.ParseHex("#FFB300"),
                    "careful"      => NextRaceTabContent.ParseHex("#FF9800"),
                    "form"         => NextRaceTabContent.ParseHex("#43A047"),
                    _              => NextRaceTabContent.ParseHex("#78909C"),
                };

        private string StrategyLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(Idea?.ViewModel?.StrategyLabel)) return Idea!.ViewModel!.StrategyLabel;
                return (Idea?.Strategy?.ToLowerInvariant()) switch
                {
                    "pitlane"      => "Pitlane",
                    "conservative" => "Conservative",
                    "careful"      => "Careful",
                    "form"         => "On Form",
                    "steady"       => "Steady",
                    _              => CapitalizeFirst(Idea?.Strategy ?? ""),
                };
            }
        }

        private string StartsInLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(Idea?.ViewModel?.StartsInLabel)) return Idea!.ViewModel!.StartsInLabel;
                if (Idea == null || string.IsNullOrEmpty(Idea.StartsAtUtc)) return "";
                if (!DateTime.TryParse(Idea.StartsAtUtc, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var when)) return Idea.StartsAtUtc;
                var local = when.ToLocalTime();
                var delta = local - DateTime.Now;
                if (delta.TotalSeconds < 0) return "Starting now";
                if (delta.TotalHours < 12) return $"in {(int)Math.Max(1, delta.TotalMinutes)} min";
                if (delta.TotalDays < 7) return local.ToString("dddd 'at' h:mm tt");
                return local.ToString("ddd MMM d 'at' h:mm tt");
            }
        }

        private string? DurationLabel
        {
            get
            {
                if (Idea == null) return null;
                if (!string.IsNullOrEmpty(Idea.ViewModel?.DurationLabel)) return Idea.ViewModel!.DurationLabel;
                if (Idea.RaceLapLimit is int laps && laps > 0) return $"{laps} laps";
                if (Idea.RaceTimeLimit is int mins && mins > 0) return $"{mins} min";
                return null;
            }
        }

        private string TrackDisplayName
        {
            get
            {
                if (Idea == null) return "";
                var basename = TrackLookup?.DisplayName ?? Idea.TrackName;
                if (!string.IsNullOrEmpty(Idea.TrackConfig)) return $"{basename} — {Idea.TrackConfig}";
                return basename;
            }
        }

        private void Rebuild()
        {
            if (Idea == null) { Content = null; return; }

            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            stack.Children.Add(BuildHero());
            stack.Children.Add(BuildBody());

            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x0A, 0x0A, 0x14)),
                Child = stack,
            };
            Content = border;
        }

        private FrameworkElement BuildHero()
        {
            var grid = new Grid { Height = 160 };

            // Base photo — track preferred, car as fallback.
            var photo = new Image
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0,
            };
            var imageUrl = TrackLookup?.ImageUrl ?? CarLookups.FirstOrDefault()?.ImageUrl;
            var photoOpacity = TrackLookup?.ImageUrl != null ? 0.25 : 0.20;
            if (!string.IsNullOrEmpty(imageUrl) && Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                photo.Source = new BitmapImage(uri);
                photo.Opacity = photoOpacity;
            }
            else
            {
                grid.Background = (Brush)Application.Current.Resources["BgElevatedBrush"];
            }
            grid.Children.Add(photo);

            // Track silhouette overlay.
            if (!string.IsNullOrEmpty(TrackLookup?.SvgPath))
            {
                grid.Children.Add(new TrackSilhouette
                {
                    SvgPath = TrackLookup!.SvgPath!,
                    StrokeBrush = new SolidColorBrush(Color.FromArgb(0x99, ScoreTint.R, ScoreTint.G, ScoreTint.B)),
                    StrokeThickness = 1.4,
                    Margin = new Thickness(24),
                });
            }

            // Bottom gradient for chip legibility.
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.5, 0),
                EndPoint = new Windows.Foundation.Point(0.5, 1),
            };
            gradient.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(0x00, 0, 0, 0) });
            gradient.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(0x8C, 0, 0, 0) });
            grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Fill = gradient,
                IsHitTestVisible = false,
            });

            // Chip layer.
            var chipLayer = new Grid
            {
                Padding = new Thickness(10),
            };
            chipLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chipLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            chipLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Top row: time chip (left) + score chip (right)
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var time = BuildTimeChip();
            Grid.SetColumn(time, 0);
            topRow.Children.Add(time);
            var score = BuildScoreChip();
            Grid.SetColumn(score, 2);
            topRow.Children.Add(score);
            Grid.SetRow(topRow, 0);
            chipLayer.Children.Add(topRow);

            // Bottom row: brand chips (BL)
            if (CarLookups.Count > 0)
            {
                var brandsRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                foreach (var c in CarLookups.Take(4))
                {
                    brandsRow.Children.Add(BuildBrandChip(c));
                }
                Grid.SetRow(brandsRow, 2);
                chipLayer.Children.Add(brandsRow);
            }

            grid.Children.Add(chipLayer);
            return grid;
        }

        private FrameworkElement BuildTimeChip()
        {
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            inner.Children.Add(new TextBlock
            {
                Text = "", // Clock glyph (Segoe Fluent)
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            });
            inner.Children.Add(new TextBlock
            {
                Text = StartsInLabel,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, ScoreTint.R, ScoreTint.G, ScoreTint.B)),
                Padding = new Thickness(10, 6, 10, 6),
                Child = inner,
                VerticalAlignment = VerticalAlignment.Top,
            };
        }

        private FrameworkElement BuildScoreChip()
        {
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            inner.Children.Add(new TextBlock
            {
                Text = "Match",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            inner.Children.Add(new TextBlock
            {
                Text = ((int)(Idea?.Score ?? 0)).ToString(),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(ScoreTint),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return new Border
            {
                CornerRadius = new CornerRadius(99),
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6),
                Child = inner,
                VerticalAlignment = VerticalAlignment.Top,
            };
        }

        private FrameworkElement BuildBrandChip(CarLookup c)
        {
            var tint = string.IsNullOrEmpty(c.Color)
                ? Color.FromArgb(0x8C, 0, 0, 0)
                : NextRaceTabContent.ParseHex(c.Color);
            var bgAlpha = string.IsNullOrEmpty(c.Color) ? (byte)0x8C : (byte)0x54;
            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(bgAlpha, tint.R, tint.G, tint.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, tint.R, tint.G, tint.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                Child = new BrandLogo { Lookup = c, GlyphHeight = 22 },
            };
        }

        private FrameworkElement BuildBody()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Padding = new Thickness(14) };

            // Track + series row
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var titleStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            titleStack.Children.Add(new TextBlock
            {
                Text = TrackDisplayName,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = Idea!.SeriesName,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            titleRow.Children.Add(titleStack);
            stack.Children.Add(titleRow);

            // License / official / fixed / duration tags row
            var tagsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (!string.IsNullOrEmpty(Idea.License)) tagsRow.Children.Add(BuildTag(Idea.License, ScoreTint, 0xCC));
            if (Idea.Official) tagsRow.Children.Add(BuildTag("Official", ScoreTint, 0x99));
            if (Idea.Fixed)    tagsRow.Children.Add(BuildTag("Fixed", ScoreTint, 0x99));
            if (DurationLabel is { } dur) tagsRow.Children.Add(BuildDurationTag(dur));
            stack.Children.Add(tagsRow);

            // Strategy pill + commentary
            var stratRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            stratRow.Children.Add(BuildStrategyPill());
            if (!string.IsNullOrEmpty(Idea.Commentary))
            {
                stratRow.Children.Add(new TextBlock
                {
                    Text = Idea.Commentary,
                    FontSize = 12,
                    MaxLines = 2,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                    VerticalAlignment = VerticalAlignment.Top,
                });
            }
            stack.Children.Add(stratRow);

            return stack;
        }

        private FrameworkElement BuildTag(string label, Color tint, byte alpha)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(alpha, tint.R, tint.G, tint.B)),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    CharacterSpacing = 40,
                },
            };
        }

        private FrameworkElement BuildDurationTag(string label)
        {
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            inner.Children.Add(new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            inner.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            return new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 3, 7, 3),
                Child = inner,
            };
        }

        private FrameworkElement BuildStrategyPill()
        {
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            inner.Children.Add(new TextBlock
            {
                Text = "", // bolt
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            });
            inner.Children.Add(new TextBlock
            {
                Text = StrategyLabel,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return new Border
            {
                CornerRadius = new CornerRadius(99),
                Background = new SolidColorBrush(StrategyColor),
                Padding = new Thickness(10, 5, 10, 5),
                Child = inner,
                VerticalAlignment = VerticalAlignment.Top,
            };
        }

        private static string CapitalizeFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }
    }
}
