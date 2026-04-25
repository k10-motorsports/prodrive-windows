using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RaceCorProDrive.Api;
using Windows.Storage.Streams;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Renders a manufacturer / brand logo from the dashboard
    /// <c>lookups</c> payload. Prefers the base64 PNG the server
    /// ships (<c>carLogos.logoPng</c>) because it's pixel-accurate to
    /// whatever the uploader seeded. Falls back to a circular initial
    /// letter tinted with the manufacturer's brand color when no PNG
    /// is available — same fallback shape used on macOS + iOS so the
    /// rail keeps its identity cues.
    /// </summary>
    public sealed class BrandLogo : UserControl
    {
        public static readonly DependencyProperty LookupProperty = DependencyProperty.Register(
            nameof(Lookup), typeof(CarLookup), typeof(BrandLogo),
            new PropertyMetadata(null, (d, _) => ((BrandLogo)d).Rebuild()));

        public static readonly DependencyProperty GlyphHeightProperty = DependencyProperty.Register(
            nameof(GlyphHeight), typeof(double), typeof(BrandLogo),
            new PropertyMetadata(20.0, (d, _) => ((BrandLogo)d).Rebuild()));

        public CarLookup? Lookup
        {
            get => (CarLookup?)GetValue(LookupProperty);
            set => SetValue(LookupProperty, value);
        }

        public double GlyphHeight
        {
            get => (double)GetValue(GlyphHeightProperty);
            set => SetValue(GlyphHeightProperty, value);
        }

        public BrandLogo()
        {
            Rebuild();
        }

        private async void Rebuild()
        {
            if (Lookup == null)
            {
                Content = null;
                return;
            }

            if (!string.IsNullOrEmpty(Lookup.LogoPng))
            {
                try
                {
                    var bytes = Convert.FromBase64String(Lookup.LogoPng);
                    var bitmap = new BitmapImage();
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    await bitmap.SetSourceAsync(stream);
                    Content = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        Height = GlyphHeight,
                    };
                    return;
                }
                catch
                {
                    // Bad base64 / corrupted PNG → fall through to initial.
                }
            }

            Content = BuildInitialBadge();
        }

        private UIElement BuildInitialBadge()
        {
            var letter = (Lookup?.Manufacturer ?? "?").Trim();
            var ch = string.IsNullOrEmpty(letter)
                ? "?"
                : letter.Substring(0, 1).ToUpperInvariant();

            var tint = ParseHexOrFallback(Lookup?.Color, "#E53935");

            var grid = new Grid
            {
                Width = GlyphHeight,
                Height = GlyphHeight,
                CornerRadius = new CornerRadius(GlyphHeight / 2),
                Background = new SolidColorBrush(tint),
            };
            var text = new TextBlock
            {
                Text = ch,
                FontSize = GlyphHeight * 0.5,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(text);
            return grid;
        }

        private static Windows.UI.Color ParseHexOrFallback(string? hex, string fallback)
        {
            var v = string.IsNullOrEmpty(hex) ? fallback : hex!;
            var trimmed = v.TrimStart('#');
            if (trimmed.Length == 6)
            {
                byte r = Convert.ToByte(trimmed.Substring(0, 2), 16);
                byte g = Convert.ToByte(trimmed.Substring(2, 2), 16);
                byte b = Convert.ToByte(trimmed.Substring(4, 2), 16);
                return Windows.UI.Color.FromArgb(0xFF, r, g, b);
            }
            return Windows.UI.Color.FromArgb(0xFF, 0xE5, 0x39, 0x35);
        }
    }
}
