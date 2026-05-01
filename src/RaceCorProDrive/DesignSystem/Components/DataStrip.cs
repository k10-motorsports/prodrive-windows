using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using RaceCorProDrive.Api;
using RaceCorProDrive.Support;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Compact horizontal stat ticker. Mirrors the SwiftUI
    /// <c>DataStrip</c>: RACES / LAPS / iRating columns per active
    /// category / SPAN / TRACKS / CARS, with a one-pixel hairline at
    /// the top edge. Pin to the bottom of a page in a Grid row.
    /// </summary>
    public sealed class DataStrip : UserControl
    {
        public static readonly DependencyProperty DashboardProperty = DependencyProperty.Register(
            nameof(Dashboard), typeof(Dashboard), typeof(DataStrip),
            new PropertyMetadata(null, (d, _) => ((DataStrip)d).Rebuild()));

        public Dashboard? Dashboard
        {
            get => (Dashboard?)GetValue(DashboardProperty);
            set => SetValue(DashboardProperty, value);
        }

        /// <summary>
        /// Active dashboard context — drives the count column's label
        /// flip ("RACES" → "PRACTICES" when context==Practice).
        /// Defaults to <see cref="DashboardContext.Race"/> for embeds
        /// that don't know about contexts.
        /// </summary>
        public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
            nameof(Context), typeof(DashboardContext), typeof(DataStrip),
            new PropertyMetadata(DashboardContext.Race, (d, _) => ((DataStrip)d).Rebuild()));

        public DashboardContext Context
        {
            get => (DashboardContext)GetValue(ContextProperty);
            set => SetValue(ContextProperty, value);
        }

        private readonly StackPanel _row;

        public DataStrip()
        {
            // 48pt strip with 8pt top + 8pt bottom internal padding.
            // Old version was 56pt with the row centered, which made
            // labels feel like they were touching the top hairline
            // and stranded a fat gap below the values. Explicit
            // top/bottom padding gives both edges symmetric breathing
            // room and shrinks the overall footer.
            Height = 48;
            VerticalAlignment = VerticalAlignment.Bottom;

            _row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 8, 16, 8),
            };

            var hairline = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 1,
                Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            var grid = new Grid();
            grid.Children.Add(_row);
            grid.Children.Add(hairline);
            Content = grid;
            Rebuild();
        }

        private void Rebuild()
        {
            _row.Children.Clear();
            var dash = Dashboard;
            // Code stays as `TotalRaces` (wire/DB field name); only
            // the on-screen label moves with context — same convention
            // codified in `prodrive-agents/skills/server-design-system`.
            var countLabel = Context == DashboardContext.Practice ? "PRACTICES" : "RACES";
            AppendStat(countLabel, dash?.Stats.TotalRaces.ToString() ?? "0");
            AppendDivider();
            AppendStat("LAPS",  dash?.Stats.TotalLaps.ToString() ?? "0");

            if (dash != null)
            {
                foreach (var entry in dash.ActiveCategories())
                {
                    AppendDivider();
                    AppendIRating(entry.Label, entry.Rating, dash.SafetyRating, entry.Key);
                }
            }

            AppendDivider();
            AppendStat("SPAN",   FormatSpan(dash?.Stats.CareerSpanDays ?? 0));
            AppendDivider();
            AppendStat("TRACKS", dash?.Stats.UniqueTracks.ToString() ?? "0");
            AppendDivider();
            AppendStat("CARS",   dash?.Stats.UniqueCars.ToString() ?? "0");
        }

        private void AppendStat(string label, string value)
        {
            var col = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Padding = new Thickness(14, 0, 14, 0),
            };
            col.Children.Add(MakeLabel(label));
            col.Children.Add(MakeValue(value));
            _row.Children.Add(col);
        }

        private void AppendIRating(
            string label,
            IRatingData rating,
            System.Collections.Generic.Dictionary<string, SafetyRatingData>? safety,
            string categoryKey)
        {
            var col = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Padding = new Thickness(14, 0, 14, 0),
            };
            col.Children.Add(MakeLabel(label));

            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
            };
            rowPanel.Children.Add(MakeValue(rating.Current?.ToString() ?? "—"));

            if (safety != null && safety.TryGetValue(categoryKey, out var sr) && sr.License is { } cls)
            {
                var srValue = sr.Current ?? 0;
                var badgeText = $"{cls}{srValue.ToString("F2", CultureInfo.InvariantCulture)}";
                rowPanel.Children.Add(MakeBadge(badgeText, LicenseTint(cls)));
            }

            // Inline sparkline matching the SwiftUI build — line color
            // tracks the trend (up=green / down=red / flat=dim), tiny
            // dot anchors the latest point.
            if (rating.Sparkline != null && rating.Sparkline.Count >= 2)
            {
                var values = new System.Collections.Generic.List<int>(rating.Sparkline.Count);
                foreach (var p in rating.Sparkline) values.Add(p.Value);
                rowPanel.Children.Add(new Sparkline
                {
                    Values = values,
                    Trend = rating.Trend,
                    Width = 110,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            col.Children.Add(rowPanel);
            _row.Children.Add(col);
        }

        private void AppendDivider()
        {
            // 28pt inside the 32pt content area (after 8+8 strip
            // padding) gives 2pt breathing room top + bottom so the
            // divider doesn't kiss the hairline above or the strip
            // floor below.
            _row.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 1,
                Height = 28,
                Fill = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        private static TextBlock MakeLabel(string text) => new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 90,
            Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
        };

        private static TextBlock MakeValue(string text) => new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
        };

        private static Border MakeBadge(string text, Color tint)
        {
            // Tighter padding + explicit center alignment on both axes.
            // Earlier version showed the digits riding the top edge of
            // the chip because TextBlock's intrinsic line-box leaves
            // descender room below — explicitly centering inside the
            // border resolves it.
            return new Border
            {
                Background = new SolidColorBrush(tint),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xD1, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextLineBounds = Microsoft.UI.Xaml.TextLineBounds.Tight,
                },
            };
        }

        private static Color LicenseTint(string cls) => cls switch
        {
            "A" => Color.FromArgb(0xFF, 0x7C, 0xDF, 0xF0),
            "B" => Color.FromArgb(0xFF, 0x6F, 0xCF, 0x69),
            "C" => Color.FromArgb(0xFF, 0xFF, 0xCF, 0x4A),
            "D" => Color.FromArgb(0xFF, 0xFF, 0x8C, 0x42),
            _   => Color.FromArgb(0xFF, 0xE5, 0x39, 0x35),
        };

        private static string FormatSpan(int days)
        {
            if (days <= 0) return "—";
            if (days < 60) return $"{days}d";
            var months = days / 30;
            if (months < 24) return $"{months}mo";
            return $"{months / 12}y";
        }
    }
}
