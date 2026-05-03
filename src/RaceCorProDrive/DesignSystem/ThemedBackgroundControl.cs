using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Full-bleed themed backdrop. Mirrors the SwiftUI
    /// <c>ThemedBackgroundView</c>: dark base color, blurred livery
    /// photo on top at low opacity, then a vertical shade gradient
    /// that helps text legibility at the bottom of the page.
    /// </summary>
    /// <remarks>
    /// Listens to <see cref="ThemeStore.Shared"/> directly so any view
    /// that drops this control on the page picks up rotations + livery
    /// changes for free. The Image's <see cref="UIElement.Opacity"/>
    /// stays at 0.7 (matches macOS) so the photo reads as ambient
    /// rather than dominant.
    /// </remarks>
    public sealed class ThemedBackgroundControl : Grid
    {
        private readonly Image _photo;
        private readonly Rectangle _gradient;

        public ThemedBackgroundControl()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0A, 0x0A, 0x14));

            _photo = new Image
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0.0, // fades in as image loads
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            // Subtle blur — matches the 16pt blur the macOS / iOS
            // implementations apply. Heavier blur creates banding on
            // some Wikimedia/Unsplash photos so we keep it modest.
            _photo.RenderTransform = new ScaleTransform { ScaleX = 1.05, ScaleY = 1.05 };

            // The bottom-half gradient is what carries the panel text
            // through bright photos — same trick the web uses.
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.5, 0.4),
                EndPoint = new Windows.Foundation.Point(0.5, 1.0),
            };
            gradientBrush.GradientStops.Add(new GradientStop
            {
                Offset = 0,
                Color = Color.FromArgb(0x00, 0x0A, 0x0A, 0x14),
            });
            gradientBrush.GradientStops.Add(new GradientStop
            {
                Offset = 1,
                Color = Color.FromArgb(0x4D, 0x0A, 0x0A, 0x14),
            });
            _gradient = new Rectangle
            {
                Fill = gradientBrush,
                IsHitTestVisible = false,
            };

            Children.Add(_photo);
            Children.Add(_gradient);

            ThemeStore.Shared.PropertyChanged += OnStoreChanged;
            UpdateImage();
        }

        private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ThemeStore.ActiveImageUri))
            {
                UpdateImage();
            }
        }

        private void UpdateImage()
        {
            if (ThemeStore.Shared.ActiveImageUri is { } uri)
            {
                _photo.Source = new BitmapImage(uri);
                _photo.Opacity = 0.7;
            }
            else
            {
                _photo.Source = null;
                _photo.Opacity = 0;
            }
        }
    }

}
