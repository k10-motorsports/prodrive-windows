using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using RaceCorProDrive.Services;
using Windows.UI;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Full-bleed app backdrop: a dark base colour, a race-car photo on top at low
    /// opacity, then a vertical shade gradient that keeps panel text legible toward the
    /// bottom. The photo is pulled at RANDOM from the car-imagery API's race set
    /// (images.racecor.io/api/v1/random?category=race) and gently crossfades to a new
    /// random car on a slow timer — replacing the old Unsplash-by-theme backdrop.
    /// </summary>
    /// <remarks>
    /// Drop this anywhere (it's mounted app-level in MainWindow, behind everything) and
    /// it self-manages: fetches on load, rotates while shown, stops when unloaded. The
    /// photo opacity stays at 0.7 so it reads as ambient, not dominant. Network failures
    /// degrade to the dark base — nothing throws.
    /// </remarks>
    public sealed class ThemedBackgroundControl : Grid
    {
        private static readonly TimeSpan RotateInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(900);
        private const double PhotoOpacity = 0.7;
        private const int RenderWidth = 2560; // full-bleed; API caps to the nearest tier

        private readonly Image _a;
        private readonly Image _b;
        private bool _frontIsA = true;
        private DispatcherQueueTimer? _timer;
        private bool _started;

        public ThemedBackgroundControl()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0A, 0x0A, 0x14));

            _a = MakePhotoLayer();
            _b = MakePhotoLayer();

            // Bottom-half gradient — carries panel text through bright photos.
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.5, 0.4),
                EndPoint = new Windows.Foundation.Point(0.5, 1.0),
            };
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(0x00, 0x0A, 0x0A, 0x14) });
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(0x4D, 0x0A, 0x0A, 0x14) });
            var gradient = new Rectangle { Fill = gradientBrush, IsHitTestVisible = false };

            Children.Add(_a);
            Children.Add(_b);
            Children.Add(gradient);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private static Image MakePhotoLayer() => new()
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Slight scale so edges never reveal letterboxing on odd aspect ratios.
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform { ScaleX = 1.05, ScaleY = 1.05 },
        };

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started) return;
            _started = true;

            _ = ShowNextAsync();

            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = RotateInterval;
            _timer.Tick += (_, __) => _ = ShowNextAsync();
            _timer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _timer = null;
            _started = false;
        }

        private async System.Threading.Tasks.Task ShowNextAsync()
        {
            var shots = await CarImageryClient.GetRandomAsync(1, RenderWidth, category: "race");
            if (shots.Count == 0) return; // keep whatever's showing (or the dark base)

            var target = _frontIsA ? _b : _a;
            var other = _frontIsA ? _a : _b;

            var bmp = new BitmapImage { DecodePixelWidth = RenderWidth };
            void OnOpened(object? s, RoutedEventArgs e)
            {
                bmp.ImageOpened -= OnOpened;
                Fade(target, target.Opacity, PhotoOpacity);
                Fade(other, other.Opacity, 0);
                _frontIsA = !_frontIsA;
            }
            bmp.ImageOpened += OnOpened;
            bmp.ImageFailed += (_, __) => bmp.ImageOpened -= OnOpened;
            bmp.UriSource = new Uri(shots[0].Url);
            target.Source = bmp;
        }

        private static void Fade(UIElement element, double from, double to)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(FadeDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(anim, element);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }
    }
}
