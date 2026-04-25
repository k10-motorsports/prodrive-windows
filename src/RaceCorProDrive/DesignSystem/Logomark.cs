using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// RaceCor logomark rendered as a composition of filled polygons,
    /// using the exact same SVG coordinate data the macOS / iOS / tvOS
    /// builds consume. Lives here so nothing about brand rendering
    /// depends on an asset-catalog pipeline — every platform draws it
    /// from first principles. Same reliability-focused pattern the
    /// native apps use (see `RaceCorLogomarkShape` in the Swift
    /// sources).
    /// </summary>
    public sealed class Logomark : UserControl
    {
        public enum Tone { White, Color }

        public static readonly DependencyProperty IconToneProperty = DependencyProperty.Register(
            nameof(IconTone), typeof(Tone), typeof(Logomark),
            new PropertyMetadata(Tone.White, (d, _) => ((Logomark)d).Rebuild()));

        public Tone IconTone
        {
            get => (Tone)GetValue(IconToneProperty);
            set => SetValue(IconToneProperty, value);
        }

        /// Native aspect — 224.3 × 93.3 — in the same viewBox the Swift
        /// build uses. Callers size via Width/Height; `Viewbox` scales
        /// the underlying Canvas so the polygon arithmetic doesn't move.
        private const double VbX = 185.3;
        private const double VbY = 258.9;
        private const double VbW = 224.3;
        private const double VbH = 93.3;

        private readonly Viewbox _viewbox;
        private readonly Canvas _canvas;

        public Logomark()
        {
            _canvas = new Canvas
            {
                Width = VbW,
                Height = VbH,
            };
            _viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = _canvas,
            };
            Content = _viewbox;
            Height = 32;   // sensible default; callers override.
            Rebuild();
        }

        private void Rebuild()
        {
            _canvas.Children.Clear();
            foreach (var (fill, points) in Polygons)
            {
                var poly = new Polygon
                {
                    Fill = new SolidColorBrush(FillFor(fill)),
                    Points = BuildPoints(points),
                };
                _canvas.Children.Add(poly);
            }
        }

        private Color FillFor(string polygonHex) => IconTone switch
        {
            Tone.Color => ParseHex(polygonHex),
            _ => Microsoft.UI.Colors.White,
        };

        private static PointCollection BuildPoints(IReadOnlyList<(double X, double Y)> pts)
        {
            var coll = new PointCollection();
            foreach (var (x, y) in pts)
            {
                coll.Add(new Point(x - VbX, y - VbY));
            }
            return coll;
        }

        private static Color ParseHex(string hex)
        {
            var s = hex.TrimStart('#');
            if (s.Length == 6)
            {
                byte r = System.Convert.ToByte(s.Substring(0, 2), 16);
                byte g = System.Convert.ToByte(s.Substring(2, 2), 16);
                byte b = System.Convert.ToByte(s.Substring(4, 2), 16);
                return Color.FromArgb(0xFF, r, g, b);
            }
            return Microsoft.UI.Colors.White;
        }

        /// Six filled polygons, same shapes the SwiftUI `RaceCorLogomarkShape`
        /// renders — they're the primitive forms of the logomark in its
        /// original SVG.
        private static readonly (string Fill, IReadOnlyList<(double X, double Y)> Points)[] Polygons =
        {
            ("#ED3733", new (double, double)[]
            {
                (351.21875, 349.621094),
                (379.378906, 349.621094),
                (407.550781, 305.335938),
                (407.550781, 304.695312),
                (380.582031, 260.863281),
                (350.019531, 260.863281),
                (380.988281, 305.816406),
            }),
            ("#711111", new (double, double)[]
            {
                (377.84375,  260.863281),
                (349.6875,   260.863281),
                (321.515625, 305.148438),
                (321.515625, 305.789062),
                (348.484375, 349.621094),
                (379.046875, 349.621094),
                (348.074219, 304.667969),
            }),
            ("#ED3733", new (double, double)[]
            {
                (273.070312, 260.863281),
                (244.910156, 260.863281),
                (244.828125, 260.992188),
                (238.203125, 260.863281),
                (223.054688, 284.605469),
                (230.117188, 284.121094),
                (224.515625, 290.898438),
                (206.445312, 290.898438),
                (187.273438, 306.226562),
                (215.648438, 305.613281),
                (216.738281, 305.789062),
                (243.710938, 349.621094),
                (271.269531, 349.621094),
                (244.300781, 304.667969),
            }),
            ("#B42226", new (double, double)[]
            {
                (301.238281, 268.25),
                (307.042969, 261.410156),
                (279.882812, 261.410156),
                (250.710938, 305.695312),
                (250.710938, 306.335938),
                (281.683594, 350.167969),
                (335.941406, 350.167969),
                (362.914062, 306.335938),
                (362.914062, 305.695312),
                (336.355469, 305.21875),
                (335.503906, 304.609375),
                (329.910156, 312.726562),
                (306.8125,   348.09375),
                (277.269531, 305.21875),
                (292.726562, 280.773438),
            }),
            ("#711111", new (double, double)[]
            {
                (314.476562, 304.667969),
                (329.855469, 282.042969),
                (302.617188, 282.042969),
                (287.917969, 305.148438),
                (287.917969, 305.789062),
                (314.886719, 349.621094),
                (345.449219, 349.621094),
            }),
            ("#711111", new (double, double)[]
            {
                (344.246094, 260.863281),
                (316.085938, 260.863281),
                (305.480469, 277.539062),
                (332.914062, 277.539062),
            }),
        };
    }
}
