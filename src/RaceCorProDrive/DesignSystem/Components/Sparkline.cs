using System;
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
    /// Tiny inline line chart for iRating history. Mirrors the SwiftUI
    /// <c>Sparkline</c>: trend-tinted line (green up / red down / dim
    /// flat), 2pt top/bottom gutter so points don't kiss the edge, and
    /// a small dot anchoring the most recent value. Drawn on a
    /// <see cref="Canvas"/> in <c>SizeChanged</c> so it reflows with
    /// the row.
    /// </summary>
    public sealed class Sparkline : UserControl
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values), typeof(IReadOnlyList<int>), typeof(Sparkline),
            new PropertyMetadata(null, (d, _) => ((Sparkline)d).Redraw()));

        public static readonly DependencyProperty TrendProperty = DependencyProperty.Register(
            nameof(Trend), typeof(int), typeof(Sparkline),
            new PropertyMetadata(0, (d, _) => ((Sparkline)d).Redraw()));

        public IReadOnlyList<int>? Values
        {
            get => (IReadOnlyList<int>?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        /// <summary>
        /// -1 / 0 / +1 — drives the line color: red (down), dim
        /// (flat), green (up). Same vocabulary the server uses.
        /// </summary>
        public int Trend
        {
            get => (int)GetValue(TrendProperty);
            set => SetValue(TrendProperty, value);
        }

        private readonly Canvas _canvas;

        public Sparkline()
        {
            _canvas = new Canvas();
            _canvas.SizeChanged += (_, __) => Redraw();
            Content = _canvas;
        }

        private Color LineColor => Trend switch
        {
            > 0 => Color.FromArgb(0xFF, 0x34, 0xC7, 0x59),  // success green
            < 0 => Color.FromArgb(0xFF, 0xE5, 0x39, 0x35),  // brand red
            _   => Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF),  // dim
        };

        private void Redraw()
        {
            _canvas.Children.Clear();
            var values = Values;
            var width = _canvas.ActualWidth;
            var height = _canvas.ActualHeight;
            if (values == null || values.Count < 2 || width <= 0 || height <= 0) return;

            int minV = int.MaxValue, maxV = int.MinValue;
            foreach (var v in values)
            {
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
            double range = Math.Max(1.0, maxV - minV);
            const double gutter = 2;
            double plotHeight = height - gutter * 2;

            Point PointAt(int i)
            {
                var x = (double)i / (values.Count - 1) * width;
                var normalized = ((double)values[i] - minV) / range;
                var y = height - gutter - normalized * plotHeight;
                return new Point(x, y);
            }

            var color = LineColor;
            var line = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            for (int i = 0; i < values.Count; i++)
            {
                line.Points.Add(PointAt(i));
            }
            _canvas.Children.Add(line);

            // Anchor dot on the most recent value — gives the eye a
            // resting point at the right edge of the trend.
            var last = PointAt(values.Count - 1);
            const double dotSize = 3;
            var dot = new Ellipse
            {
                Width = dotSize * 2,
                Height = dotSize * 2,
                Fill = new SolidColorBrush(color),
            };
            Canvas.SetLeft(dot, last.X - dotSize);
            Canvas.SetTop(dot, last.Y - dotSize);
            _canvas.Children.Add(dot);
        }
    }
}
