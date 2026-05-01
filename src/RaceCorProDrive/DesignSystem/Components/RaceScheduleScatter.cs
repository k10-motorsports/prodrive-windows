using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using RaceCorProDrive.Api;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Race schedule scatter chart. Mirrors the SwiftUI
    /// <c>RaceScheduleScatter</c>: time-of-day on Y (3AM…9PM ticks) and
    /// day-of-week on X (Mon-Sun); dot size scales with session count,
    /// color scales with avg incidents per bucket using the web's red
    /// ramp.
    /// </summary>
    /// <remarks>
    /// Outer chrome (panel + header + body host) is built once in the
    /// ctor so the inner Canvas keeps a stable parent — re-parenting a
    /// UIElement that already lives in another visual tree throws on
    /// WinUI, which is what crashed the page when the dashboard
    /// poller raised PropertyChanged twice.
    /// </remarks>
    public sealed class RaceScheduleScatter : UserControl
    {
        public static readonly DependencyProperty BucketsProperty = DependencyProperty.Register(
            nameof(Buckets), typeof(IReadOnlyList<ScatterBucket>), typeof(RaceScheduleScatter),
            new PropertyMetadata(null, (d, _) => ((RaceScheduleScatter)d).OnBucketsChanged()));

        public IReadOnlyList<ScatterBucket>? Buckets
        {
            get => (IReadOnlyList<ScatterBucket>?)GetValue(BucketsProperty);
            set => SetValue(BucketsProperty, value);
        }

        // Server buckets use JS day convention (0=Sun … 6=Sat); the
        // web's display order is Mon..Sun so we remap at render time.
        private static readonly int[] DisplayDayOrder = { 1, 2, 3, 4, 5, 6, 0 };
        private static readonly string[] DayLabels = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        private static readonly (int Hour, string Label)[] HourTicks =
        {
            (3,  "3 AM"),
            (6,  "6 AM"),
            (9,  "9 AM"),
            (12, "12 PM"),
            (15, "3 PM"),
            (18, "6 PM"),
            (21, "9 PM"),
        };

        private const int MinHour = 0;
        private const int MaxHour = 24;
        private const double MinRadius = 4;
        private const double MaxRadius = 14;

        private readonly Canvas _plot;
        private readonly Canvas _yAxisCanvas;
        private readonly ContentPresenter _bodyHost;
        private readonly Grid _plotBody;        // Stable parent for _plot + day labels.
        private readonly Border _emptyState;    // Stable empty-state element.

        public RaceScheduleScatter()
        {
            // ── Y-axis ────────────────────────────────────────────
            _yAxisCanvas = new Canvas { Width = 36, Height = 260 };
            _yAxisCanvas.SizeChanged += (_, __) => RepositionYLabels();
            foreach (var t in HourTicks)
            {
                _yAxisCanvas.Children.Add(new TextBlock
                {
                    Text = t.Label,
                    FontSize = 9,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                    Tag = t.Hour,
                });
            }

            // ── Plot canvas ───────────────────────────────────────
            _plot = new Canvas
            {
                Height = 260,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _plot.SizeChanged += (_, __) => DrawPlot();

            // ── Day-label row ─────────────────────────────────────
            var dayLabelsRow = new Grid
            {
                Margin = new Thickness(MaxRadius + 2, 4, MaxRadius + 2, 0),
            };
            for (int i = 0; i < 7; i++)
            {
                dayLabelsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var lbl = new TextBlock
                {
                    Text = DayLabels[i],
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    CharacterSpacing = 60,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                Grid.SetColumn(lbl, i);
                dayLabelsRow.Children.Add(lbl);
            }

            // ── Stable plot body (Y-axis | plot+day-labels) ───────
            _plotBody = new Grid { ColumnSpacing = 8 };
            _plotBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            _plotBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_yAxisCanvas, 0);
            _plotBody.Children.Add(_yAxisCanvas);
            var plotStack = new Grid();
            plotStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            plotStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_plot, 0);
            Grid.SetRow(dayLabelsRow, 1);
            plotStack.Children.Add(_plot);
            plotStack.Children.Add(dayLabelsRow);
            Grid.SetColumn(plotStack, 1);
            _plotBody.Children.Add(plotStack);

            // ── Empty state ───────────────────────────────────────
            _emptyState = new Border
            {
                Height = 220,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x4D, 0x0A, 0x0A, 0x14)),
                Child = new TextBlock
                {
                    Text = "Run a few sessions to chart your schedule",
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["TextMutedBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            // ── Body host (swaps between empty + plot) ────────────
            _bodyHost = new ContentPresenter { Content = _emptyState };

            // ── Outer chrome (built once) ─────────────────────────
            var outer = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
            outer.Children.Add(BuildHeader());
            outer.Children.Add(_bodyHost);
            // See RaceCalendarHeatmap — no outer Padding on Panel,
            // it offsets the visible card edge from the UserControl
            // bounds and breaks left-edge alignment with sibling cards.
            Content = new Panel { Content = outer };
        }

        private FrameworkElement BuildHeader()
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "", // Clock glyph
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 0);
            header.Children.Add(icon);

            var title = new TextBlock
            {
                Text = "Race Schedule",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);

            var legend = new TextBlock
            {
                Text = "Incidents",
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(legend, 3);
            header.Children.Add(legend);
            return header;
        }

        private void OnBucketsChanged()
        {
            var hasData = Buckets != null && Buckets.Count > 0;
            _bodyHost.Content = hasData ? (FrameworkElement)_plotBody : _emptyState;
            if (hasData) DrawPlot();
        }

        private void RepositionYLabels()
        {
            var height = _yAxisCanvas.ActualHeight;
            if (height <= 0) return;
            var plotInset = MaxRadius + 2;
            var plotHeight = height - plotInset * 2;
            foreach (var child in _yAxisCanvas.Children)
            {
                if (child is TextBlock tb && tb.Tag is int hour)
                {
                    var y = plotInset + YForHour(hour, plotHeight);
                    Canvas.SetTop(tb, y - 6);
                    Canvas.SetLeft(tb, 0);
                }
            }
        }

        private void DrawPlot()
        {
            _plot.Children.Clear();
            var width = _plot.ActualWidth;
            var height = _plot.ActualHeight;
            if (width <= 0 || height <= 0 || Buckets == null || Buckets.Count == 0) return;

            var plotInset = MaxRadius + 2;
            var plotWidth = width - plotInset * 2;
            var plotHeight = height - plotInset * 2;
            var colW = plotWidth / 7;

            foreach (var t in HourTicks)
            {
                var y = plotInset + YForHour(t.Hour, plotHeight);
                var line = new Line
                {
                    X1 = plotInset, X2 = plotInset + plotWidth,
                    Y1 = y, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 0.5,
                };
                _plot.Children.Add(line);
            }

            var maxCount = Math.Max(1, Buckets.Max(b => b.Count));
            var minInc = Buckets.Min(b => b.AvgIncidents);
            var maxInc = Buckets.Max(b => b.AvgIncidents);

            foreach (var bucket in Buckets)
            {
                if (bucket.Count <= 0) continue;
                var column = Array.IndexOf(DisplayDayOrder, bucket.Day);
                if (column < 0) continue;
                var x = plotInset + colW * column + colW / 2;
                var y = plotInset + YForHour(bucket.Hour, plotHeight);
                var sizeT = Math.Sqrt((double)bucket.Count / maxCount);
                var radius = MinRadius + (MaxRadius - MinRadius) * sizeT;

                var dot = new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = new SolidColorBrush(IncidentColor(bucket.AvgIncidents, minInc, maxInc)),
                };
                Canvas.SetLeft(dot, x - radius);
                Canvas.SetTop(dot, y - radius);
                _plot.Children.Add(dot);
            }
        }

        private static double YForHour(int hour, double height)
        {
            var range = (double)(MaxHour - MinHour);
            return ((hour - MinHour) / range) * height;
        }

        private static Color IncidentColor(double avg, double min, double max)
        {
            var range = max - min;
            var t = range > 0 ? (avg - min) / range : 0;
            (double r, double g, double b) low  = (0.44, 0.00, 0.06);
            (double r, double g, double b) mid  = (0.69, 0.13, 0.13);
            (double r, double g, double b) high = (0.90, 0.22, 0.21);
            (double r, double g, double b) c;
            if (t < 0.5)
            {
                var f = t / 0.5;
                c = (low.r + (mid.r - low.r) * f,
                     low.g + (mid.g - low.g) * f,
                     low.b + (mid.b - low.b) * f);
            }
            else
            {
                var f = (t - 0.5) / 0.5;
                c = (mid.r + (high.r - mid.r) * f,
                     mid.g + (high.g - mid.g) * f,
                     mid.b + (high.b - mid.b) * f);
            }
            return Color.FromArgb(0xFF,
                (byte)Math.Round(c.r * 255),
                (byte)Math.Round(c.g * 255),
                (byte)Math.Round(c.b * 255));
        }
    }
}
