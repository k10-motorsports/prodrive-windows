using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Api;
using RaceCorProDrive.DesignSystem;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Support;
using Panel = RaceCorProDrive.DesignSystem.Components.Panel;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Signed-in dashboard. Reads live data from
    /// <see cref="DashboardPoller.Shared"/>; rebuilds the Performance
    /// tab's panels (Should-You-Race, Strengths/Watch-Out, plus
    /// stubbed visualization placeholders pending the Win2D port) on
    /// every <see cref="INotifyPropertyChanged"/> tick. NextRace and
    /// Previous tab content is stubbed for v1 and will follow.
    /// </summary>
    public sealed partial class DashboardPage : Page
    {
        public DashboardPage()
        {
            this.InitializeComponent();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            DashboardPoller.Shared.PropertyChanged += OnPollerChanged;
            DashboardPoller.Shared.Start();
            await ThemeStore.Shared.RefreshAsync();
            UpdateForCurrentSnapshot();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DashboardPoller.Shared.PropertyChanged -= OnPollerChanged;
            // Keep polling alive even when this Page is unloaded so
            // notifications + cached state survive nav transitions.
            // Sign-out flows call Stop() explicitly.
        }

        private void OnPollerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardPoller.Latest))
            {
                DispatcherQueue.TryEnqueue(UpdateForCurrentSnapshot);
            }
        }

        private void OnTabChanged(object? sender, DashboardTabs.Tab tab)
            => UpdateForCurrentSnapshot();

        private void UpdateForCurrentSnapshot()
        {
            var dash = DashboardPoller.Shared.Latest;
            StatStrip.Dashboard = dash;
            Sidebar.Dashboard = dash;

            LoadingRing.IsActive = dash == null;
            MainContent.Children.Clear();

            switch (TabsBar.Selected)
            {
                case DashboardTabs.Tab.Performance:
                    PopulatePerformance(dash);
                    break;
                case DashboardTabs.Tab.NextRace:
                    PopulatePlaceholder("Next Race", "Race-suggestion ideas land here once the Performance tab feels complete.");
                    break;
                case DashboardTabs.Tab.Previous:
                    PopulatePlaceholder("Previous Races", "Recent race rows will populate here from the dashboard payload's recentSessions field.");
                    break;
            }
        }

        // MARK: - Performance tab

        private void PopulatePerformance(Dashboard? dash)
        {
            MainContent.Children.Add(BuildShouldYouRace(dash));
            MainContent.Children.Add(BuildStrengthsWatchOut(dash));
            MainContent.Children.Add(BuildVizPlaceholder("Race Calendar"));
            MainContent.Children.Add(BuildVizPlaceholder("Race Schedule"));
            MainContent.Children.Add(BuildVizPlaceholder("Driver DNA"));
        }

        private static ShouldYouRacePanel BuildShouldYouRace(Dashboard? dash)
        {
            var panel = new ShouldYouRacePanel();

            if (dash?.When is { } when)
            {
                var hour = DateTime.Now.Hour;
                var inPeak = IsInWindow(hour, when.PeakHourStart, when.WindowSize);
                var inWorst = IsInWindow(hour, when.WorstHourStart, when.WindowSize);

                var verdict =
                    inPeak ? Verdict.Good :
                    inWorst ? Verdict.Bad :
                    Verdict.Marginal;

                panel.Verdict = verdict;
                panel.Title = inPeak ? "Go for it"
                            : inWorst ? "Skip this window"
                            : "Marginal";
                panel.Reason = inPeak ? $"You're in your peak window ({when.PeakHours})."
                              : inWorst ? $"This is your weakest window ({when.WorstHours})."
                              : "Not your best or worst — okay to race but temper expectations.";

                if (dash.WhenPanel is { } wp)
                {
                    panel.Detail = new ShouldYouRacePanel.DetailModel
                    {
                        PeakWindow = wp.Strengths is { } s ? $"{s.Days}, {s.Hours}" : null,
                        PeakDelta = wp.Strengths?.AvgIRatingDelta is double sd ? FormatDelta(sd) : null,
                        WorstWindow = wp.WatchOut is { } w ? $"{w.Days}, {w.Hours}" : null,
                        WorstDelta = wp.WatchOut?.AvgIRatingDelta is double wd ? FormatDelta(wd) : null,
                        NowDescription = NowAnnotation(hour, inPeak, inWorst),
                    };
                }
            }
            else
            {
                panel.Verdict = Verdict.Unknown;
                panel.Title = "Not enough data";
                panel.Reason = "Add a few more races to get a race-now advisory.";
            }
            return panel;
        }

        private static StrengthsWatchOutPanel BuildStrengthsWatchOut(Dashboard? dash)
        {
            var panel = new StrengthsWatchOutPanel();
            var success = Microsoft.UI.Colors.LimeGreen;
            var brand = Windows.UI.Color.FromArgb(0xFF, 0xE5, 0x39, 0x35);

            if (dash?.WhenPanel is { Strengths: { } s, WatchOut: { } w })
            {
                panel.Strengths = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "STRENGTHS",
                    Tint = Windows.UI.Color.FromArgb(0xFF, 0x34, 0xC7, 0x59),
                    Window = $"{s.Days}\n{s.Hours}",
                    Footnote = s.AvgIRatingDelta is double d ? FormatDelta(d) : null,
                    Summary = s.Paragraph,
                    Bullets = s.Bullets.ConvertAll(b => b.Text),
                };
                panel.WatchOut = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "WATCH OUT",
                    Tint = brand,
                    Window = $"{w.Days}\n{w.Hours}",
                    Footnote = w.AvgIRatingDelta is double dw ? FormatDelta(dw) : null,
                    Summary = w.Paragraph,
                    Bullets = w.Bullets.ConvertAll(b => b.Text),
                };
            }
            else
            {
                panel.Strengths = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "STRENGTHS",
                    Tint = Windows.UI.Color.FromArgb(0xFF, 0x34, 0xC7, 0x59),
                    Window = "Collecting data",
                    Summary = "Run 5+ sessions to see your strongest time windows.",
                };
                panel.WatchOut = new StrengthsWatchOutPanel.SideModel
                {
                    Label = "WATCH OUT",
                    Tint = brand,
                    Window = "Collecting data",
                    Summary = "Incident hotspots surface once more sessions are logged.",
                };
            }
            return panel;
        }

        /// <summary>
        /// Until the Win2D port lands, the three viz cards (calendar /
        /// schedule / DNA) render as panel placeholders so the layout
        /// rhythm matches the macOS dashboard top-to-bottom and we
        /// can swap each one in independently.
        /// </summary>
        private static Panel BuildVizPlaceholder(string title)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextPrimaryBrush"],
            });
            stack.Children.Add(new Border
            {
                Height = 96,
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BgBaseBrush"],
                Opacity = 0.3,
                Child = new TextBlock
                {
                    Text = "Coming soon",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMutedBrush"],
                },
            });
            return new Panel { Content = stack, Padding = new Thickness(14) };
        }

        // MARK: - Other tabs (placeholders for v1)

        private void PopulatePlaceholder(string title, string sub)
        {
            MainContent.Children.Add(new Panel
            {
                Padding = new Thickness(40),
                Content = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title.ToUpperInvariant(),
                            FontSize = 11,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            CharacterSpacing = 200,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextDimBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = sub,
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextSecondaryBrush"],
                        },
                    },
                },
            });
        }

        // MARK: - Helpers

        private static bool IsInWindow(int hour, int start, int size)
        {
            for (int i = 0; i < size; i++)
            {
                var h = (start + i) % 24;
                if (h == hour) return true;
            }
            return false;
        }

        private static string FormatDelta(double delta)
        {
            var sign = delta > 0 ? "+" : "";
            return $"avg {sign}{(int)System.Math.Round(delta)} iR";
        }

        private static string NowAnnotation(int hour, bool inPeak, bool inWorst)
        {
            var time = DateTime.Now.ToString("h:mm tt 'on' dddd", CultureInfo.InvariantCulture);
            if (inPeak) return $"Currently {time} — inside your peak window.";
            if (inWorst) return $"Currently {time} — inside your weakest window.";
            return $"Currently {time} — outside both windows.";
        }
    }
}
