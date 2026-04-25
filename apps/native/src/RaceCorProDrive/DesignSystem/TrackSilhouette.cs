using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Renders a track silhouette by parsing the SVG <c>d=</c> string
    /// the server ships in the dashboard <c>lookups</c> payload. The
    /// paths live in a normalized 0–100 viewBox (see <c>track-svg.ts</c>
    /// on the server) so a simple Viewbox scales them to any display
    /// size without distortion.
    /// </summary>
    /// <remarks>
    /// Windows XAML's <see cref="Microsoft.UI.Xaml.Media.Geometry"/>
    /// mini-language matches the SVG path command set we emit (M, L,
    /// H, V, C, Q, Z) — the Swift builds had to hand-roll a parser
    /// because SwiftUI's `Path` doesn't accept string paths, but here
    /// we can hand the same string straight to XAML.
    /// </remarks>
    public sealed class TrackSilhouette : UserControl
    {
        public static readonly DependencyProperty SvgPathProperty = DependencyProperty.Register(
            nameof(SvgPath), typeof(string), typeof(TrackSilhouette),
            new PropertyMetadata(string.Empty, (d, _) => ((TrackSilhouette)d).Rebuild()));

        public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
            nameof(StrokeBrush), typeof(Brush), typeof(TrackSilhouette),
            new PropertyMetadata(null, (d, _) => ((TrackSilhouette)d).Rebuild()));

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            nameof(StrokeThickness), typeof(double), typeof(TrackSilhouette),
            new PropertyMetadata(1.2, (d, _) => ((TrackSilhouette)d).Rebuild()));

        public string SvgPath
        {
            get => (string)GetValue(SvgPathProperty);
            set => SetValue(SvgPathProperty, value);
        }

        public Brush? StrokeBrush
        {
            get => (Brush?)GetValue(StrokeBrushProperty);
            set => SetValue(StrokeBrushProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        private readonly Viewbox _viewbox;
        private readonly Canvas _canvas;
        private readonly Path _path;

        public TrackSilhouette()
        {
            _path = new Path
            {
                Stretch = Stretch.None,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeThickness = 1.2,
            };
            _canvas = new Canvas
            {
                Width = 100,
                Height = 100,
                Children = { _path },
            };
            _viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = _canvas,
            };
            Content = _viewbox;
        }

        private void Rebuild()
        {
            if (string.IsNullOrEmpty(SvgPath))
            {
                _path.Data = null;
                return;
            }
            // `(Geometry)XamlBindingHelper.ConvertValue(...)` is how
            // WinUI 3 converts a Path Markup Syntax string to a
            // Geometry object outside of XAML. Same syntax the server
            // emits — no intermediate processing needed.
            _path.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper
                .ConvertValue(typeof(Geometry), SvgPath);
            _path.Stroke = StrokeBrush ?? (Brush)Application.Current.Resources["TextSecondaryBrush"];
            _path.StrokeThickness = StrokeThickness;
        }
    }
}
