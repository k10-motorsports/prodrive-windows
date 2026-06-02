using System;
using System.Collections.Generic;
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
    /// 8-axis polar radar rendered from server-pre-computed values.
    /// Mirrors the SwiftUI <c>DriverDNARadar</c>: archetype header
    /// (variant + major + optional description) above a polygon
    /// drawn over four concentric grid rings + spokes. Server's
    /// <c>computeDriverDNA</c> + <c>getDriverArchetype</c> drive
    /// every axis value and label — zero client-side computation.
    /// </summary>
    /// <remarks>
    /// Outer chrome (panel + header + archetype host + canvas) is
    /// built once in the ctor; only the archetype text and the
    /// canvas children are updated on Dashboard property change.
    /// Re-parenting a UIElement that already lives in another
    /// visual tree throws on WinUI, which is what crashed the page
    /// when the dashboard poller raised PropertyChanged twice.
    /// </remarks>
    public sealed class DriverDNARadar : UserControl
    {
        public static readonly DependencyProperty DashboardProperty = DependencyProperty.Register(
            nameof(Dashboard), typeof(Dashboard), typeof(DriverDNARadar),
            new PropertyMetadata(null, (d, _) => ((DriverDNARadar)d).OnDashboardChanged()));

        public Dashboard? Dashboard
        {
            get => (Dashboard?)GetValue(DashboardProperty);
            set => SetValue(DashboardProperty, value);
        }

        /// <summary>
        /// Drawing canvas height. Default 280 (matches dashboard 3-up
        /// viz row). The DNA hero page bumps this to ~480 for a
        /// larger, more readable radar.
        /// </summary>
        public double RadarHeight
        {
            get => _canvas.Height;
            set
            {
                _canvas.Height = value;
                DrawRadar();
            }
        }

        private readonly Canvas _canvas;
        private readonly TextBlock _archPrimary;
        private readonly TextBlock _archSecondary;
        private readonly TextBlock _archDescription;

        public DriverDNARadar()
        {
            // ── Header ────────────────────────────────────────────
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            headerRow.Children.Add(new TextBlock
            {
                Text = "", // Connect/atom-ish glyph
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = "Driver DNA",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });

            // ── Archetype block (text mutates) ────────────────────
            _archPrimary = new TextBlock
            {
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            };
            _archSecondary = new TextBlock
            {
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _archDescription = new TextBlock
            {
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                Visibility = Visibility.Collapsed,
            };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(_archPrimary);
            titleRow.Children.Add(_archSecondary);
            var archStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            archStack.Children.Add(titleRow);
            archStack.Children.Add(_archDescription);

            // ── Canvas (children rebuilt on data / size change) ───
            // Height defaults to 280 — the size the dashboard's 3-up
            // viz row uses. The DNA page bumps this up for a hero-
            // sized radar via the RadarHeight setter.
            _canvas = new Canvas { Height = 280 };
            _canvas.SizeChanged += (_, __) => DrawRadar();

            // ── Stable outer chrome ───────────────────────────────
            var outer = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
            outer.Children.Add(headerRow);
            outer.Children.Add(archStack);
            outer.Children.Add(_canvas);
            // See RaceCalendarHeatmap — no outer Padding on Panel,
            // it offsets the visible card edge from the UserControl
            // bounds and breaks left-edge alignment with sibling cards.
            Content = new Panel { Content = outer };

            ApplyArchetype();
        }

        private void OnDashboardChanged()
        {
            ApplyArchetype();
            DrawRadar();
        }

        private void ApplyArchetype()
        {
            var (primary, secondary, description) = GetArchetype();
            _archPrimary.Text = primary;
            _archSecondary.Text = secondary;
            if (string.IsNullOrEmpty(description))
            {
                _archDescription.Text = "";
                _archDescription.Visibility = Visibility.Collapsed;
            }
            else
            {
                _archDescription.Text = description;
                _archDescription.Visibility = Visibility.Visible;
            }
        }

        private (string primary, string secondary, string? description) GetArchetype()
        {
            var a = Dashboard?.DriverArchetype;
            if (a == null) return ("Learning the ropes", "Rookie", null);
            return (a.Variant, a.Major, a.VariantDescription);
        }

        private List<(string Label, double Value)> GetAxes()
        {
            var d = Dashboard?.DriverDNA;
            if (d == null)
            {
                return new List<(string, double)>
                {
                    ("Consistency", 0), ("Racecraft", 0), ("Cleanness", 0), ("Endurance", 0),
                    ("Adaptability", 0), ("Improvement", 0), ("Wet", 0), ("Experience", 0),
                };
            }
            return new List<(string, double)>
            {
                ("Consistency",  d.Consistency),
                ("Racecraft",    d.Racecraft),
                ("Cleanness",    d.Cleanness),
                ("Endurance",    d.Endurance),
                ("Adaptability", d.Adaptability),
                ("Improvement",  d.Improvement),
                ("Wet",          d.WetWeather),
                ("Experience",   d.Experience),
            };
        }

        private void DrawRadar()
        {
            _canvas.Children.Clear();
            var width = _canvas.ActualWidth;
            var height = _canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var axes = GetAxes();
            var count = axes.Count;
            if (count < 3) return;

            var side = Math.Min(Math.Min(width, height), 420);
            var center = new Point(width / 2, height / 2);
            var radius = side / 2 - 32;
            if (radius <= 0) return;

            Point AxisPoint(int i, double distance)
            {
                var angle = i / (double)count * 2 * Math.PI - Math.PI / 2;
                return new Point(
                    center.X + Math.Cos(angle) * distance,
                    center.Y + Math.Sin(angle) * distance);
            }

            var ringBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            foreach (var ring in new[] { 0.25, 0.5, 0.75, 1.0 })
            {
                var poly = new Polygon
                {
                    Stroke = ringBrush,
                    StrokeThickness = 0.5,
                };
                for (int i = 0; i < count; i++)
                {
                    poly.Points.Add(AxisPoint(i, radius * ring));
                }
                _canvas.Children.Add(poly);
            }

            var spokeBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            for (int i = 0; i < count; i++)
            {
                var pt = AxisPoint(i, radius);
                _canvas.Children.Add(new Line
                {
                    X1 = center.X, Y1 = center.Y,
                    X2 = pt.X, Y2 = pt.Y,
                    Stroke = spokeBrush,
                    StrokeThickness = 0.5,
                });
            }

            var brand = Color.FromArgb(0xFF, 0xE5, 0x39, 0x35);
            var data = new Polygon
            {
                Stroke = new SolidColorBrush(brand),
                StrokeThickness = 1.2,
                Fill = new SolidColorBrush(Color.FromArgb(0x47, brand.R, brand.G, brand.B)),
            };
            for (int i = 0; i < count; i++)
            {
                var v = Math.Max(0, Math.Min(100, axes[i].Value));
                data.Points.Add(AxisPoint(i, radius * (v / 100.0)));
            }
            _canvas.Children.Add(data);

            for (int i = 0; i < count; i++)
            {
                var pt = AxisPoint(i, radius + 12);
                var label = new TextBlock
                {
                    Text = axes[i].Label,
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var dx = label.DesiredSize.Width / 2;
                var dy = label.DesiredSize.Height / 2;
                Canvas.SetLeft(label, pt.X - dx);
                Canvas.SetTop(label, pt.Y - dy);
                _canvas.Children.Add(label);
            }
        }
    }
}
