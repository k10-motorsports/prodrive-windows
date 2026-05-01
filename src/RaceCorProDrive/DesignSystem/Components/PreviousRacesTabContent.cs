using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using RaceCorProDrive.Api;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Previous Races tab — server-baked card list grouped by session
    /// type (Races / Practice / Time Trials), with a Cards / List view
    /// toggle. Mirrors the SwiftUI <c>PreviousRacesTabContent</c>: the
    /// server's <c>viewModel</c> drives every label, position color,
    /// and tab assignment so web and native can't drift.
    /// </summary>
    public sealed class PreviousRacesTabContent : UserControl
    {
        public static readonly DependencyProperty CardsProperty = DependencyProperty.Register(
            nameof(Cards), typeof(IReadOnlyList<PreviousRaceCard>), typeof(PreviousRacesTabContent),
            new PropertyMetadata(null, (d, _) => ((PreviousRacesTabContent)d).Rebuild()));

        public static readonly DependencyProperty LookupsProperty = DependencyProperty.Register(
            nameof(Lookups), typeof(DashboardLookups), typeof(PreviousRacesTabContent),
            new PropertyMetadata(null, (d, _) => ((PreviousRacesTabContent)d).Rebuild()));

        public IReadOnlyList<PreviousRaceCard>? Cards
        {
            get => (IReadOnlyList<PreviousRaceCard>?)GetValue(CardsProperty);
            set => SetValue(CardsProperty, value);
        }

        public DashboardLookups? Lookups
        {
            get => (DashboardLookups?)GetValue(LookupsProperty);
            set => SetValue(LookupsProperty, value);
        }

        private enum SessionTab { Races, Practices, TimeTrials }
        private enum Mode { Cards, List }

        private SessionTab _activeTab = SessionTab.Races;
        private Mode _mode = Mode.Cards;

        private readonly StackPanel _root;
        private readonly Grid _controlsRow;
        private readonly StackPanel _tabSegment;
        private readonly StackPanel _modeSegment;
        private readonly ContentPresenter _bodyHost;

        public PreviousRacesTabContent()
        {
            _tabSegment = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            _modeSegment = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            _bodyHost = new ContentPresenter();
            _controlsRow = new Grid();

            _controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tabBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Child = _tabSegment,
            };
            Grid.SetColumn(tabBorder, 0);
            _controlsRow.Children.Add(tabBorder);

            var modeBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Child = _modeSegment,
            };
            Grid.SetColumn(modeBorder, 2);
            _controlsRow.Children.Add(modeBorder);

            _root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14 };
            _root.Children.Add(_controlsRow);
            _root.Children.Add(_bodyHost);
            Content = _root;

            Rebuild();
        }

        private void Rebuild()
        {
            BuildControlsContent();
            BuildBodyContent();
        }

        private Dictionary<SessionTab, List<PreviousRaceCard>> Group()
        {
            var result = new Dictionary<SessionTab, List<PreviousRaceCard>>
            {
                [SessionTab.Races]      = new(),
                [SessionTab.Practices]  = new(),
                [SessionTab.TimeTrials] = new(),
            };
            if (Cards == null) return result;
            foreach (var c in Cards)
            {
                switch (c.ViewModel.SessionTypeTab)
                {
                    case "practices":   result[SessionTab.Practices].Add(c); break;
                    case "time_trials": result[SessionTab.TimeTrials].Add(c); break;
                    default:            result[SessionTab.Races].Add(c); break;
                }
            }
            return result;
        }

        private void BuildControlsContent()
        {
            _tabSegment.Children.Clear();
            _modeSegment.Children.Clear();

            var grouped = Group();
            void AddTabButton(SessionTab tab, string label, string glyph)
            {
                var count = grouped.TryGetValue(tab, out var items) ? items.Count : 0;
                var isActive = _activeTab == tab;
                _tabSegment.Children.Add(BuildSegmentItem(label, glyph, count, isActive, () =>
                {
                    _activeTab = tab;
                    Rebuild();
                }));
            }
            AddTabButton(SessionTab.Races,      "RACES",      "");
            AddTabButton(SessionTab.Practices,  "PRACTICE",   "");
            AddTabButton(SessionTab.TimeTrials, "TIME TRIALS", "");

            void AddModeButton(Mode m, string label, string glyph)
            {
                var isActive = _mode == m;
                _modeSegment.Children.Add(BuildSegmentItem(label, glyph, count: 0, isActive: isActive, onTap: () =>
                {
                    _mode = m;
                    Rebuild();
                }));
            }
            AddModeButton(Mode.Cards, "CARDS", "");
            AddModeButton(Mode.List,  "LIST",  "");
        }

        private FrameworkElement BuildSegmentItem(string label, string glyph, int count, bool isActive, Action onTap)
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (!string.IsNullOrEmpty(glyph))
            {
                content.Children.Add(new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 11,
                    Foreground = isActive
                        ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                        : (Brush)Application.Current.Resources["TextDimBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 60,
                Foreground = isActive
                    ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (count > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = isActive
                        ? (Brush)Application.Current.Resources["TextSecondaryBrush"]
                        : (Brush)Application.Current.Resources["TextMutedBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var border = new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF))
                    : null,
                Child = content,
            };
            border.Tapped += (_, __) => onTap();
            return border;
        }

        private void BuildBodyContent()
        {
            if (Cards == null || Cards.Count == 0)
            {
                _bodyHost.Content = BuildEmptyAllState();
                return;
            }

            var grouped = Group();
            var active = grouped[_activeTab];
            if (active.Count == 0)
            {
                _bodyHost.Content = BuildEmptyTabState();
                return;
            }

            _bodyHost.Content = _mode == Mode.Cards
                ? BuildCardsGrid(active)
                : BuildListView(active);
        }

        private FrameworkElement BuildCardsGrid(List<PreviousRaceCard> items)
        {
            var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < items.Count; i++)
            {
                var card = items[i];
                var row = i / 2;
                var col = i % 2;
                if (row >= grid.RowDefinitions.Count)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                var view = new PreviousRaceCardView
                {
                    Card = card,
                    Lookups = Lookups,
                };
                Grid.SetColumn(view, col);
                Grid.SetRow(view, row);
                grid.Children.Add(view);
            }
            return grid;
        }

        private FrameworkElement BuildListView(List<PreviousRaceCard> items)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            for (int i = 0; i < items.Count; i++)
            {
                stack.Children.Add(new PreviousRaceListRow
                {
                    Card = items[i],
                    Lookups = Lookups,
                });
                if (i < items.Count - 1)
                {
                    stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        Height = 1,
                        Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                    });
                }
            }
            return new Panel { Padding = new Thickness(0), Content = stack };
        }

        private FrameworkElement BuildEmptyAllState()
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
                Text = "No completed races yet",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            inner.Children.Add(new TextBlock
            {
                Text = "Race results will appear here once sessions are imported.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return new Panel { Padding = new Thickness(40), Content = inner };
        }

        private FrameworkElement BuildEmptyTabState()
        {
            var kind = _activeTab switch
            {
                SessionTab.Practices  => "practices",
                SessionTab.TimeTrials => "time trials",
                _                     => "races",
            };
            var inner = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            inner.Children.Add(new TextBlock
            {
                Text = $"No {kind} recorded yet",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return new Panel { Padding = new Thickness(30), Content = inner };
        }
    }

    /// <summary>
    /// Card-style Previous Races entry. Renders the server's
    /// <c>viewModel</c> verbatim — no client-side formatting. Layout
    /// mirrors the SwiftUI <c>PreviousRaceCardView</c>: 140-tall hero
    /// header with track image + silhouette + game/brand chips, then
    /// a body with track + car + giant position badge + optional
    /// practice/qualifying chips + footer.
    /// </summary>
    public sealed class PreviousRaceCardView : UserControl
    {
        public PreviousRaceCard? Card { get; set; }
        public DashboardLookups? Lookups { get; set; }

        public PreviousRaceCardView()
        {
            Loaded += (_, __) => Rebuild();
        }

        private TrackLookup? Track => Card != null ? Lookups?.TrackFor(Card.Session.TrackName) : null;
        private CarLookup? Car => Card != null ? Lookups?.CarFor(Card.Session.CarModel) : null;

        private Color PositionColor =>
            !string.IsNullOrEmpty(Card?.ViewModel?.PositionColorHex)
                ? NextRaceTabContent.ParseHex(Card!.ViewModel!.PositionColorHex)
                : Color.FromArgb(0xFF, 0xB0, 0xB0, 0xB0);

        private void Rebuild()
        {
            if (Card == null) { Content = null; return; }

            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            stack.Children.Add(BuildHero());
            stack.Children.Add(BuildBody());

            Content = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x0A, 0x0A, 0x14)),
                Child = stack,
            };
        }

        private FrameworkElement BuildHero()
        {
            var grid = new Grid { Height = 140 };

            var photo = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            var url = Track?.ImageUrl ?? Car?.ImageUrl;
            var photoOpacity = Track?.ImageUrl != null ? 0.25 : 0.20;
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                photo.Source = new BitmapImage(uri);
                photo.Opacity = photoOpacity;
            }
            else
            {
                grid.Background = (Brush)Application.Current.Resources["BgElevatedBrush"];
            }
            grid.Children.Add(photo);

            if (!string.IsNullOrEmpty(Track?.SvgPath))
            {
                grid.Children.Add(new TrackSilhouette
                {
                    SvgPath = Track!.SvgPath!,
                    StrokeBrush = new SolidColorBrush(Color.FromArgb(0x8C, 0xE5, 0x39, 0x35)),
                    StrokeThickness = 1.4,
                    Margin = new Thickness(20),
                });
            }

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

            var chipLayer = new Grid { Padding = new Thickness(10) };
            chipLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chipLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            chipLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topRow = new StackPanel { Orientation = Orientation.Horizontal };
            topRow.Children.Add(BuildGameChip());
            Grid.SetRow(topRow, 0);
            chipLayer.Children.Add(topRow);

            if (Car != null)
            {
                var bottomRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                bottomRow.Children.Add(BuildBrandChip(Car));
                Grid.SetRow(bottomRow, 2);
                chipLayer.Children.Add(bottomRow);
            }

            grid.Children.Add(chipLayer);
            return grid;
        }

        private FrameworkElement BuildGameChip()
        {
            return new Border
            {
                CornerRadius = new CornerRadius(99),
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = Card!.ViewModel.GameLabel,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    CharacterSpacing = 50,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
                },
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
                Child = new BrandLogo { Lookup = c, GlyphHeight = 28 },
            };
        }

        private FrameworkElement BuildBody()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Padding = new Thickness(14) };

            // Title row + giant position badge
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            titleStack.Children.Add(new TextBlock
            {
                Text = Track?.DisplayName ?? Card!.ViewModel.TrackLabel,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = Card!.Session.CarModel,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(titleStack, 0);
            titleRow.Children.Add(titleStack);

            var posBadge = BuildPositionBadge();
            Grid.SetColumn(posBadge, 1);
            titleRow.Children.Add(posBadge);
            stack.Children.Add(titleRow);

            // Practice / qualifying chips
            if (Card.PracticeSession != null || Card.QualifyingSession != null)
            {
                var preStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
                if (!string.IsNullOrEmpty(Card.ViewModel.PracticeBestLapLabel))
                {
                    preStack.Children.Add(BuildPreRaceRow(
                        title: "PRACTICE",
                        primary: Card.ViewModel.PracticeBestLapLabel!,
                        secondary: null,
                        tintBrush: (Brush)Application.Current.Resources["BorderSubtleBrush"],
                        note: Card.ViewModel.PracticeIncidentsLabel));
                }
                if (!string.IsNullOrEmpty(Card.ViewModel.QualifyingBestLapLabel))
                {
                    preStack.Children.Add(BuildPreRaceRow(
                        title: "QUALIFYING",
                        primary: Card.ViewModel.QualifyingBestLapLabel!,
                        secondary: Card.ViewModel.QualifyingPositionLabel,
                        tintBrush: new SolidColorBrush(Color.FromArgb(0x40, 0xF5, 0xCA, 0x47)),
                        note: null));
                }
                stack.Children.Add(preStack);
            }

            stack.Children.Add(BuildFooter());

            return stack;
        }

        private FrameworkElement BuildPositionBadge()
        {
            var display = Card!.ViewModel.PositionDisplay;
            if (string.IsNullOrEmpty(display)) return new Grid();

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 0,
            };

            if (display!.StartsWith("P", StringComparison.OrdinalIgnoreCase))
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 1,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                row.Children.Add(new TextBlock
                {
                    Text = "P",
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB3, PositionColor.R, PositionColor.G, PositionColor.B)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
                row.Children.Add(new TextBlock
                {
                    Text = display.Substring(1),
                    FontSize = 36,
                    FontWeight = Microsoft.UI.Text.FontWeights.Black,
                    Foreground = new SolidColorBrush(PositionColor),
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
                stack.Children.Add(row);
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = display,
                    FontSize = 22,
                    FontWeight = Microsoft.UI.Text.FontWeights.Black,
                    Foreground = new SolidColorBrush(PositionColor),
                    HorizontalAlignment = HorizontalAlignment.Right,
                });
            }

            if (!string.IsNullOrEmpty(Card.ViewModel.FieldSizeLabel))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = Card.ViewModel.FieldSizeLabel,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x99, PositionColor.R, PositionColor.G, PositionColor.B)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                });
            }

            return stack;
        }

        private FrameworkElement BuildPreRaceRow(string title, string primary, string? secondary, Brush tintBrush, string? note)
        {
            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            rowGrid.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 60,
                Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });

            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (!string.IsNullOrEmpty(secondary))
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = secondary,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(NextRaceTabContent.ParseHex("#F5CA47")),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            rightStack.Children.Add(new TextBlock
            {
                Text = primary,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(rightStack, 1);
            rowGrid.Children.Add(rightStack);

            var inner = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
            inner.Children.Add(rowGrid);
            if (!string.IsNullOrEmpty(note))
            {
                inner.Children.Add(new TextBlock
                {
                    Text = note,
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                });
            }

            return new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderBrush = tintBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                Child = inner,
            };
        }

        private FrameworkElement BuildFooter()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            leftRow.Children.Add(new TextBlock
            {
                Text = Card!.ViewModel.DateLabel,
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            leftRow.Children.Add(new TextBlock
            {
                Text = "·",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            leftRow.Children.Add(new TextBlock
            {
                Text = Card.ViewModel.SessionLabel,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(leftRow, 0);
            topRow.Children.Add(leftRow);

            if (!string.IsNullOrEmpty(Card.ViewModel.BestLapLabel) && Card.ViewModel.BestLapLabel != "—")
            {
                var lap = new TextBlock
                {
                    Text = Card.ViewModel.BestLapLabel,
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(lap, 1);
                topRow.Children.Add(lap);
            }
            stack.Children.Add(topRow);

            if (!string.IsNullOrEmpty(Card.ViewModel.IncidentsLabel))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = Card.ViewModel.IncidentsLabel,
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                });
            }
            return stack;
        }
    }

    /// <summary>
    /// Compact list row for the Previous Races "List" view. Mirrors
    /// the SwiftUI <c>PreviousRaceListRow</c>: position / track + car
    /// / date + best lap.
    /// </summary>
    public sealed class PreviousRaceListRow : UserControl
    {
        public PreviousRaceCard? Card { get; set; }
        public DashboardLookups? Lookups { get; set; }

        public PreviousRaceListRow()
        {
            Loaded += (_, __) => Rebuild();
        }

        private TrackLookup? Track => Card != null ? Lookups?.TrackFor(Card.Session.TrackName) : null;

        private Color PositionColor =>
            !string.IsNullOrEmpty(Card?.ViewModel?.PositionColorHex)
                ? NextRaceTabContent.ParseHex(Card!.ViewModel!.PositionColorHex)
                : Color.FromArgb(0xFF, 0xB0, 0xB0, 0xB0);

        private void Rebuild()
        {
            if (Card == null) { Content = null; return; }

            var grid = new Grid { ColumnSpacing = 14, Padding = new Thickness(14, 12, 14, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(BuildPosition());

            var middle = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            middle.Children.Add(new TextBlock
            {
                Text = Track?.DisplayName ?? Card.ViewModel.TrackLabel,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            middle.Children.Add(new TextBlock
            {
                Text = Card.Session.CarModel,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(middle, 1);
            grid.Children.Add(middle);

            var right = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock
            {
                Text = Card.ViewModel.DateLabel,
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
            });
            if (!string.IsNullOrEmpty(Card.ViewModel.BestLapLabel) && Card.ViewModel.BestLapLabel != "—")
            {
                right.Children.Add(new TextBlock
                {
                    Text = Card.ViewModel.BestLapLabel,
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                    HorizontalAlignment = HorizontalAlignment.Right,
                });
            }
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            Content = grid;
        }

        private FrameworkElement BuildPosition()
        {
            var text = new TextBlock
            {
                Text = Card!.ViewModel.PositionDisplay ?? "—",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(PositionColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return text;
        }
    }
}
