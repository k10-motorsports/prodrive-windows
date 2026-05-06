using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Renders a lucide-matching icon by pointing <see cref="SvgImageSource"/>
    /// at one of the bundled SVGs under <c>Assets/LucideIcons/</c>. Matches
    /// the web (lucide-react) + native (SwiftUI Paths) visual language so
    /// the three platforms feel like the same product.
    /// </summary>
    /// <remarks>
    /// Why not PathIcon + inline Geometry: WinUI's PathIcon rasterizes a
    /// single Geometry stroke, but lucide icons are a mix of paths,
    /// circles, and rounded rects. SVG rendering via SvgImageSource
    /// preserves the multi-primitive structure with no conversion work.
    /// Stroke color is baked into the files at build time (#E8E8F0) so
    /// the icons read correctly on the dark palette without a
    /// currentColor-replacing pass at runtime; dim states are handled
    /// via <see cref="UIElement.Opacity"/> on the hosting control.
    /// </remarks>
    public sealed class LucideIcon : UserControl
    {
        private readonly Image _image;

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind),
            typeof(string),
            typeof(LucideIcon),
            new PropertyMetadata(string.Empty, OnKindChanged));

        public string Kind
        {
            get => (string)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public LucideIcon()
        {
            // Default display size matches lucide's native viewBox; XAML
            // callers override with Width/Height as needed.
            Width = 24;
            Height = 24;
            _image = new Image
            {
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
            };
            Content = _image;
        }

        private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LucideIcon icon)
            {
                icon.UpdateSource();
            }
        }

        private void UpdateSource()
        {
            if (string.IsNullOrEmpty(Kind))
            {
                _image.Source = null;
                return;
            }

            var uri = new Uri($"ms-appx:///Assets/LucideIcons/{Kind}.svg");
            _image.Source = new SvgImageSource(uri);
        }
    }

    /// <summary>
    /// Well-known lucide icon slugs. Matching these against file names
    /// under <c>Assets/LucideIcons/</c> keeps everything string-addressed
    /// without scattering typos throughout the codebase.
    /// </summary>
    public static class LucideIconKind
    {
        public const string Dashboard    = "layout-dashboard";
        public const string PitWall      = "launcher-option-1-gauge";
        public const string Dna          = "dna";
        public const string When         = "calendar-clock";
        public const string Tracks       = "map-pin";
        public const string Cars         = "car";
        public const string Moments      = "sparkles";
        public const string Races        = "flag";
        public const string Profile      = "user";
        public const string Settings     = "settings";
        public const string LaunchHud    = "launcher-option-1-gauge";
        public const string Library      = "film";
    }
}
