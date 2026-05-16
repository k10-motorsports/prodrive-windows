using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using RaceCorProDrive.Api;
using RaceCorProDrive.DesignSystem.Components;
using RaceCorProDrive.Support;
using Panel = RaceCorProDrive.DesignSystem.Components.Panel;

namespace RaceCorProDrive.Pages
{
    /// <summary>
    /// Driver DNA hero page. Subscribes to <see cref="DashboardPoller.Shared"/>
    /// (the same singleton DashboardPage uses) so the eight axes,
    /// archetype, and descriptions all come from the
    /// server-computed <c>driverDNA</c> / <c>driverArchetype</c>
    /// fields already on the Dashboard payload — no extra fetch.
    ///
    /// Previously this destination routed to PlaceholderPage and
    /// showed "Coming soon"; the data was already on the wire, it
    /// just wasn't being rendered.
    /// </summary>
    public sealed partial class DnaPage : Page
    {
        public DnaPage()
        {
            this.InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // No tabs / filters on this page — clear the titlebar slots
            // in case a previous page (dashboard, library) left widgets.
            App.MainWindow?.SetTitleBarTabs(null);
            App.MainWindow?.SetTitleBarFilters(null);

            DashboardPoller.Shared.PropertyChanged += OnPollerChanged;
            DashboardPoller.Shared.Start();
            Render();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DashboardPoller.Shared.PropertyChanged -= OnPollerChanged;
            // Don't stop polling — DashboardPage's status indicator
            // wants updates too. Same pattern as Settings/Dashboard.
        }

        private void OnPollerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardPoller.Latest))
            {
                DispatcherQueue.TryEnqueue(Render);
            }
        }

        // ── Rendering ───────────────────────────────────────────────

        private void Render()
        {
            var dash = DashboardPoller.Shared.Latest;
            Body.Children.Clear();

            if (dash == null)
            {
                Body.Children.Add(BuildLoadingCard());
                StatusCaption.Text = "Loading your DNA from the server…";
                return;
            }

            if (dash.DriverDNA == null)
            {
                Body.Children.Add(BuildEmptyCard());
                StatusCaption.Text = "Not enough sessions yet. Run a few more races and your DNA will populate.";
                return;
            }

            StatusCaption.Text = "Your eight-axis profile across consistency, racecraft, cleanness, endurance, adaptability, improvement, wet weather, and experience.";

            // Archetype description card (top, full-width).
            var archCard = BuildArchetypeCard(dash.DriverArchetype);
            if (archCard != null) Body.Children.Add(archCard);

            // Radar + axis breakdown side-by-side.
            Body.Children.Add(BuildRadarAndBreakdown(dash));
        }

        private static UIElement BuildLoadingCard()
        {
            var ring = new ProgressRing
            {
                IsActive = true,
                Width = 24,
                Height = 24,
                Foreground = (Brush)Application.Current.Resources["BrandRedBrush"],
            };
            var label = new TextBlock
            {
                Text = "Fetching driver DNA…",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            row.Children.Add(ring);
            row.Children.Add(label);

            return new Panel { Content = row };
        }

        private static UIElement BuildEmptyCard()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            stack.Children.Add(new TextBlock
            {
                Text = "DNA not ready",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Your DNA is computed from completed sessions and rating history. After a handful of races, the server has enough signal to score all eight axes.",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            });
            return new Panel { Content = stack };
        }

        // Archetype card: major + variant names + the long-form
        // descriptions. Server populates both *Description fields when
        // it has enough signal; collapse to a single-line title row if
        // descriptions are missing.
        private static UIElement? BuildArchetypeCard(DriverArchetypeData? a)
        {
            if (a == null
                || (string.IsNullOrEmpty(a.Major) && string.IsNullOrEmpty(a.Variant)))
            {
                return null;
            }

            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };

            stack.Children.Add(new TextBlock
            {
                Text = "ARCHETYPE",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 200,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 0, 0, 4),
            });

            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
            };
            titleRow.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(a.Variant) ? a.Major : a.Variant,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            });
            if (!string.IsNullOrEmpty(a.Variant) && !string.IsNullOrEmpty(a.Major))
            {
                titleRow.Children.Add(new TextBlock
                {
                    Text = a.Major,
                    FontSize = 14,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 3),
                });
            }
            stack.Children.Add(titleRow);

            if (!string.IsNullOrEmpty(a.VariantDescription))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = a.VariantDescription,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                });
            }
            if (!string.IsNullOrEmpty(a.MajorDescription)
                && a.MajorDescription != a.VariantDescription)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = a.MajorDescription,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                });
            }

            return new Panel { Content = stack };
        }

        // 50/50 row — radar on the left, axis breakdown on the right.
        private static UIElement BuildRadarAndBreakdown(Dashboard dash)
        {
            var grid = new Grid { ColumnSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Reuse the existing radar component at a larger canvas
            // height — the dashboard 3-up grid uses 280; the hero page
            // gets 480 (radar then scales up to the component's
            // internal 420pt soft cap).
            var radar = new DriverDNARadar
            {
                Dashboard = dash,
                RadarHeight = 480,
            };
            Grid.SetColumn(radar, 0);
            grid.Children.Add(radar);

            var breakdown = BuildAxisBreakdown(dash.DriverDNA!);
            Grid.SetColumn(breakdown, 1);
            grid.Children.Add(breakdown);

            return grid;
        }

        private static UIElement BuildAxisBreakdown(DriverDNAData d)
        {
            // Same axes + order as the radar (keeps the two visuals
            // reading top-to-bottom in lockstep).
            var rows = new List<(string Label, double Value)>
            {
                ("Consistency",  d.Consistency),
                ("Racecraft",    d.Racecraft),
                ("Cleanness",    d.Cleanness),
                ("Endurance",    d.Endurance),
                ("Adaptability", d.Adaptability),
                ("Improvement",  d.Improvement),
                ("Wet weather",  d.WetWeather),
                ("Experience",   d.Experience),
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
            stack.Children.Add(new TextBlock
            {
                Text = "AXES",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 200,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 0, 0, 4),
            });

            foreach (var (label, raw) in rows)
            {
                stack.Children.Add(BuildAxisRow(label, raw));
            }

            return new Panel { Content = stack };
        }

        private static UIElement BuildAxisRow(string label, double rawValue)
        {
            // Clamp to 0..100 — the radar does the same. Server should
            // never return out-of-range numbers but defending against
            // it keeps the bar from underflowing/overflowing on a
            // partial response.
            var value = Math.Max(0, Math.Min(100, rawValue));

            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(labelText, 0);
            row.Children.Add(labelText);

            // Bar: dark track + brand-red fill. ProgressBar would work
            // but its template ships with platform-specific theming
            // that fights the dashboard's flat-dark look; the manual
            // Rectangle pair matches the rest of the design system.
            var track = new Rectangle
            {
                Height = 6,
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var fill = new Rectangle
            {
                Height = 6,
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0x39, 0x35)),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            // Pin the fill's width to a fraction of the track via a
            // Grid-of-one — the parent Grid column is "1*", so binding
            // by ratio is cleanest via SizeChanged.
            var barHost = new Grid();
            barHost.Children.Add(track);
            barHost.Children.Add(fill);
            barHost.SizeChanged += (s, _) =>
            {
                var w = ((Grid)s).ActualWidth;
                fill.Width = Math.Max(0, w * value / 100.0);
            };
            Grid.SetColumn(barHost, 1);
            barHost.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(barHost);

            var valueText = new TextBlock
            {
                Text = ((int)Math.Round(value)).ToString(),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);

            return row;
        }
    }
}
