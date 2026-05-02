using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Small read-only line graph for a Moza pedal curve. The plugin
    /// reports each pedal's response curve as 5 points (input% → output%);
    /// this draws those 5 points onto a 0..100 grid with a dotted linear
    /// reference line behind. Pure visualization, no editing — that comes
    /// in a later PR once the per-car settings concept lands.
    /// </summary>
    public sealed class PedalCurveView : UserControl
    {
        public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
            nameof(Points), typeof(IReadOnlyList<int>), typeof(PedalCurveView),
            new PropertyMetadata(null, (d, _) => ((PedalCurveView)d).Redraw()));

        /// <summary>
        /// Curve sample values, expected length 5. Each value 0..100.
        /// Index i represents input fraction i/4 (0%, 25%, 50%, 75%, 100%).
        /// </summary>
        public IReadOnlyList<int>? Points
        {
            get => (IReadOnlyList<int>?)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        private readonly Canvas _canvas;

        public PedalCurveView()
        {
            Width = 180;
            Height = 90;
            _canvas = new Canvas
            {
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            };
            Content = new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = _canvas,
            };
            SizeChanged += (_, __) => Redraw();
        }

        private void Redraw()
        {
            _canvas.Children.Clear();
            var pts = Points;
            var w = ActualWidth - 2;
            var h = ActualHeight - 2;
            if (w <= 0 || h <= 0) return;

            // Linear reference (dashed, dim). Draws the y=x identity so
            // a flat 0/25/50/75/100 curve shows the pedal is unmodified.
            var reference = new Line
            {
                X1 = 0, Y1 = h,
                X2 = w, Y2 = 0,
                Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
            };
            _canvas.Children.Add(reference);

            if (pts == null || pts.Count == 0) return;

            var poly = new Polyline
            {
                Stroke = (Brush)Application.Current.Resources["BrandRedBrush"],
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
            };
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                double xi = (double)i / (n - 1);
                double yi = System.Math.Clamp(pts[i] / 100.0, 0.0, 1.0);
                var x = xi * w;
                var y = h - yi * h;
                poly.Points.Add(new Point(x, y));
            }
            _canvas.Children.Add(poly);
        }
    }
}
