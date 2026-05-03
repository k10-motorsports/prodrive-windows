using System;
using System.ComponentModel;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Full-bleed themed backdrop. Mirrors the SwiftUI
    /// <c>ThemedBackgroundView</c>: dark base color, blurred livery
    /// photo at low opacity, then a vertical shade gradient that helps
    /// text legibility at the bottom of the page.
    /// </summary>
    /// <remarks>
    /// The blur uses the WinUI 3 composition pipeline with Win2D's
    /// <see cref="GaussianBlurEffect"/> against a
    /// <see cref="LoadedImageSurface"/>. Doing it via Composition keeps
    /// the blur GPU-resident and DPI-aware — a CPU-side pre-blur
    /// (WriteableBitmap or ImageSharp) would break on DPI changes and
    /// cost us the cross-fade.
    ///
    /// Mac uses a 6pt SwiftUI .blur(radius:6) in points; the Composition
    /// blur amount is in pixels, and Windows backgrounds run at higher
    /// DPI than the typical Mac display — 8 reads similarly.
    ///
    /// Listens to <see cref="ThemeStore.Shared"/> directly so any view
    /// that drops this control on the page picks up rotations + livery
    /// changes for free.
    /// </remarks>
    public sealed class ThemedBackgroundControl : Grid
    {
        private const float BlurRadius = 8f;
        private const float OpaqueAlpha = 0.7f;

        private readonly Border _photoHost;
        private readonly Rectangle _gradient;
        private SpriteVisual? _photoVisual;
        private CompositionEffectBrush? _photoBrush;
        private LoadedImageSurface? _currentSurface;

        public ThemedBackgroundControl()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0A, 0x0A, 0x14));

            // The composition sprite goes into this Border's child
            // visual slot. Border participates in XAML layout so we get
            // SizeChanged + DPI-aware coordinates for free.
            _photoHost = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
            };
            _photoHost.SizeChanged += (_, __) => UpdateVisualSize();

            // Bottom-half gradient — helps text legibility at the foot
            // of the view without hiding the image.
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

            Children.Add(_photoHost);
            Children.Add(_gradient);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // ── Lifecycle ───────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ThemeStore.Shared.PropertyChanged += OnStoreChanged;
            UpdateImage();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeStore.Shared.PropertyChanged -= OnStoreChanged;
            DisposeSurface();
            // Clear the child visual to release the SpriteVisual.
            ElementCompositionPreview.SetElementChildVisual(_photoHost, null);
            _photoVisual = null;
            _photoBrush = null;
        }

        private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ThemeStore.ActiveImageUri))
            {
                UpdateImage();
            }
        }

        // ── Composition pipeline ────────────────────────────────

        private void UpdateImage()
        {
            var uri = ThemeStore.Shared.ActiveImageUri;
            if (uri == null)
            {
                FadeOut();
                return;
            }

            // (Re)build the surface for the new image. We always swap
            // surfaces — caching across themes isn't worth the complexity;
            // theme rotations are infrequent and surfaces release on GC.
            DisposeSurface();
            _currentSurface = LoadedImageSurface.StartLoadFromUri(uri);
            _currentSurface.LoadCompleted += OnSurfaceLoaded;

            EnsureVisual();
            // Rebuild the brush so the new surface is wired in. Effect
            // factories compile shaders on first use; reusing the
            // factory across image swaps would be cheaper but the
            // overhead is microseconds and theme swaps are rare.
            BuildPhotoBrush(_currentSurface);
        }

        private void OnSurfaceLoaded(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
        {
            if (args.Status != LoadedImageSourceLoadStatus.Success) return;
            // Cross-fade in. Match Mac's 0.6s ease-in-out.
            FadeIn();
            UpdateVisualSize();
        }

        private void EnsureVisual()
        {
            if (_photoVisual != null) return;
            var compositor = ElementCompositionPreview.GetElementVisual(_photoHost).Compositor;
            _photoVisual = compositor.CreateSpriteVisual();
            _photoVisual.Opacity = 0f; // fades in once the surface lands
            ElementCompositionPreview.SetElementChildVisual(_photoHost, _photoVisual);
        }

        private void BuildPhotoBrush(LoadedImageSurface surface)
        {
            if (_photoVisual == null) return;
            var compositor = _photoVisual.Compositor;

            // Source the image, blur it, then tint to OpaqueAlpha so the
            // dark base color reads through. Doing the alpha multiply
            // inside the effect graph (rather than via Visual.Opacity)
            // keeps the blur boundary clean.
            var graph = new BlendEffect
            {
                Mode = BlendEffectMode.Multiply,
                Background = new ColorSourceEffect
                {
                    Color = Color.FromArgb((byte)(OpaqueAlpha * 255), 0xFF, 0xFF, 0xFF),
                },
                Foreground = new GaussianBlurEffect
                {
                    BlurAmount = BlurRadius,
                    BorderMode = EffectBorderMode.Hard,
                    Source = new CompositionEffectSourceParameter("Image"),
                },
            };

            var factory = compositor.CreateEffectFactory(graph);
            var brush = factory.CreateBrush();
            brush.SetSourceParameter("Image", compositor.CreateSurfaceBrush(surface));

            _photoBrush = brush;
            _photoVisual.Brush = brush;
        }

        private void UpdateVisualSize()
        {
            if (_photoVisual == null) return;
            _photoVisual.Size = new Vector2((float)_photoHost.ActualWidth, (float)_photoHost.ActualHeight);
        }

        private void FadeIn()
        {
            if (_photoVisual == null) return;
            var compositor = _photoVisual.Compositor;
            var anim = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1f, 1f);
            anim.Duration = TimeSpan.FromMilliseconds(600);
            _photoVisual.StartAnimation(nameof(Visual.Opacity), anim);
        }

        private void FadeOut()
        {
            if (_photoVisual == null) return;
            var compositor = _photoVisual.Compositor;
            var anim = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1f, 0f);
            anim.Duration = TimeSpan.FromMilliseconds(600);
            _photoVisual.StartAnimation(nameof(Visual.Opacity), anim);
        }

        private void DisposeSurface()
        {
            if (_currentSurface != null)
            {
                _currentSurface.LoadCompleted -= OnSurfaceLoaded;
                _currentSurface.Dispose();
                _currentSurface = null;
            }
        }
    }
}
