using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using RaceCorProDrive.Services;
using Windows.Foundation;
using Windows.UI;
// Colors is intentionally fully-qualified as Microsoft.UI.Colors at the use sites —
// both Microsoft.UI and Windows.UI define a Colors type, so an unqualified `using`
// would make `Colors` ambiguous (same pattern as LeftRail). `Color` = Windows.UI.Color.

namespace RaceCorProDrive.Screensaver
{
    /// <summary>
    /// Full-screen "photo wall" screensaver: a grid of car photographs pulled at
    /// random from the car-imagery API (images.racecor.io), gently cross-fading tile
    /// by tile. Manually launched from the LeftRail; it is NEVER shown during a sim
    /// session — the rail button is hidden while iRacing runs, and if the sim launches
    /// while the wall is up it closes itself (see <see cref="OnRacingStateChanged"/>).
    /// Any key / click / real mouse move dismisses it.
    ///
    /// Built programmatically (no XAML) to match the DesignSystem component style and
    /// keep the whole feature self-contained.
    /// </summary>
    public sealed class ScreensaverWindow : Window
    {
        // Visual + timing knobs.
        private const int TargetTilePx = 520;     // ~physical px per tile → drives the grid size
        private const int MinCols = 4, MaxCols = 8, MinRows = 3, MaxRows = 6;
        private static readonly TimeSpan RotateInterval = TimeSpan.FromSeconds(2.5); // one tile at a time
        private static readonly TimeSpan PoolRefresh = TimeSpan.FromMinutes(3);      // fresh randomness
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(700);
        private static readonly TimeSpan InputGrace = TimeSpan.FromMilliseconds(900); // ignore cursor settle
        private const double MoveDismissPx = 18;  // mouse travel (DIPs) that counts as "user is back"

        private readonly Grid _root;
        private readonly Random _rng = new();
        private readonly List<Tile> _tiles = new();
        private List<CarImageryClient.CarShot> _pool = new();
        private int _poolCursor;
        private int _tilePx = 640;

        private DispatcherQueueTimer? _rotateTimer;
        private DispatcherQueueTimer? _refreshTimer;

        private readonly Stopwatch _shownFor = Stopwatch.StartNew();
        private bool _havePointerAnchor;
        private Point _pointerAnchor;
        private bool _closed;

        public ScreensaverWindow()
        {
            Title = "Screensaver";

            // Chromeless, edge-to-edge. FullScreen presenter hides the title bar and
            // caption buttons entirely — exactly what a screensaver wants.
            if (AppWindow is not null)
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }

            _root = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
                // Focusable so the wall receives key events for dismissal.
                IsTabStop = true,
            };
            _root.PointerPressed += (_, __) => Dismiss();
            _root.PointerMoved += OnPointerMoved;
            _root.KeyDown += (_, e) => { e.Handled = true; Dismiss(); };
            Content = _root;

            // Close the wall the moment a sim session begins.
            IRacingDetector.Shared.PropertyChanged += OnRacingStateChanged;
            Closed += OnClosed;

            // Build once the window is realized + sized (AppWindow.Size is reliable then).
            Activated += OnFirstActivated;
        }

        private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
        {
            Activated -= OnFirstActivated;
            _root.Focus(FocusState.Programmatic);
            _ = InitAsync();
        }

        private async System.Threading.Tasks.Task InitAsync()
        {
            // Grid size from the actual screen. Fall back to 1080p if the size isn't
            // populated yet for any reason.
            var size = AppWindow?.Size ?? default;
            int w = size.Width > 0 ? size.Width : 1920;
            int h = size.Height > 0 ? size.Height : 1080;

            int cols = Math.Clamp((int)Math.Round((double)w / TargetTilePx), MinCols, MaxCols);
            int rows = Math.Clamp((int)Math.Round((double)h / TargetTilePx), MinRows, MaxRows);
            _tilePx = Math.Max(320, w / cols);

            BuildGrid(cols, rows);

            // Pull ~2× tiles for rotation variety (API caps count at 60).
            int tileCount = cols * rows;
            int want = Math.Clamp(tileCount * 2, 12, 60);
            _pool = new List<CarImageryClient.CarShot>(
                await CarImageryClient.GetRandomAsync(want, NearestWidth(_tilePx)));

            if (_closed) return;

            if (_pool.Count == 0)
            {
                ShowMessage("No connection to the image library.");
                return;
            }

            Shuffle(_pool);
            // Seed every tile.
            foreach (var tile in _tiles)
                SetTileImage(tile, NextShot(), animate: false);

            StartTimers();
        }

        // ── Layout ─────────────────────────────────────────────────────────────

        private void BuildGrid(int cols, int rows)
        {
            var grid = new Grid();
            for (int c = 0; c < cols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < rows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var tile = MakeTile();
                Grid.SetColumn(tile.Container, c);
                Grid.SetRow(tile.Container, r);
                grid.Children.Add(tile.Container);
                _tiles.Add(tile);
            }

            _root.Children.Add(grid);
        }

        private Tile MakeTile()
        {
            // Two stacked image layers for a true cross-fade (load into the hidden
            // layer, fade it in over the visible one, then swap).
            var back = MakeImageLayer();
            var front = MakeImageLayer();
            front.Opacity = 0;

            var credit = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(10, 0, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Opacity = 0.85,
            };

            // Subtle bottom scrim so the credit stays legible over bright photos.
            var scrim = new Border
            {
                Height = 56,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(0x00, 0, 0, 0), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(0x99, 0, 0, 0), Offset = 1 },
                    },
                },
            };

            var container = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x08, 0x08, 0x10)),
                Margin = new Thickness(1),
            };
            container.Children.Add(back);
            container.Children.Add(front);
            container.Children.Add(scrim);
            container.Children.Add(credit);

            // Clip overflow from UniformToFill cropping.
            container.SizeChanged += (s, e) =>
            {
                container.Clip = new RectangleGeometry
                {
                    Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
                };
            };

            return new Tile { Container = container, Back = back, Front = front, Credit = credit, FrontVisible = false };
        }

        private static Image MakeImageLayer() => new()
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // ── Rotation ───────────────────────────────────────────────────────────

        private void StartTimers()
        {
            _rotateTimer = DispatcherQueue.CreateTimer();
            _rotateTimer.Interval = RotateInterval;
            _rotateTimer.Tick += (_, __) => RotateOneTile();
            _rotateTimer.Start();

            _refreshTimer = DispatcherQueue.CreateTimer();
            _refreshTimer.Interval = PoolRefresh;
            _refreshTimer.Tick += async (_, __) =>
            {
                var fresh = await CarImageryClient.GetRandomAsync(_pool.Count, NearestWidth(_tilePx));
                if (_closed || fresh.Count == 0) return;
                _pool = new List<CarImageryClient.CarShot>(fresh);
                Shuffle(_pool);
                _poolCursor = 0;
            };
            _refreshTimer.Start();
        }

        private void RotateOneTile()
        {
            if (_closed || _tiles.Count == 0 || _pool.Count == 0) return;
            var tile = _tiles[_rng.Next(_tiles.Count)];
            SetTileImage(tile, NextShot(), animate: true);
        }

        private CarImageryClient.CarShot NextShot()
        {
            var shot = _pool[_poolCursor % _pool.Count];
            _poolCursor++;
            return shot;
        }

        private void SetTileImage(Tile tile, CarImageryClient.CarShot shot, bool animate)
        {
            // Decode at display size to keep memory sane on a wall of tiles.
            var bmp = new BitmapImage { DecodePixelWidth = _tilePx };

            // Target the currently-hidden layer; fade it in over the visible one.
            var target = tile.FrontVisible ? tile.Back : tile.Front;
            var other = tile.FrontVisible ? tile.Front : tile.Back;

            void OnOpened(object? s, RoutedEventArgs e)
            {
                bmp.ImageOpened -= OnOpened;
                if (_closed) return;
                tile.Credit.Text = FormatCredit(shot);
                if (animate)
                {
                    Fade(target, 0, 1);
                    Fade(other, other.Opacity, 0);
                }
                else
                {
                    target.Opacity = 1;
                    other.Opacity = 0;
                }
                tile.FrontVisible = !tile.FrontVisible;
            }

            bmp.ImageOpened += OnOpened;
            bmp.ImageFailed += (_, __) => bmp.ImageOpened -= OnOpened; // skip on failure
            // Setting UriSource kicks off the async decode → ImageOpened.
            bmp.UriSource = new Uri(shot.Url);
            target.Source = bmp;
        }

        private void Fade(UIElement element, double from, double to)
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

        private static string FormatCredit(CarImageryClient.CarShot shot)
        {
            if (string.IsNullOrEmpty(shot.DisplayName) && string.IsNullOrEmpty(shot.Credit))
                return string.Empty;
            if (string.IsNullOrEmpty(shot.Credit)) return shot.DisplayName;
            if (string.IsNullOrEmpty(shot.DisplayName)) return $"Photo: {shot.Credit}";
            return $"{shot.DisplayName}  ·  Photo: {shot.Credit}";
        }

        private void ShowMessage(string text)
        {
            _root.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // ── Dismissal + race gating ──────────────────────────────────────────────

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            // Ignore the cursor settling for the first moment after launch.
            if (_shownFor.Elapsed < InputGrace) return;
            var p = e.GetCurrentPoint(_root).Position;
            if (!_havePointerAnchor)
            {
                _havePointerAnchor = true;
                _pointerAnchor = p;
                return;
            }
            if (Math.Abs(p.X - _pointerAnchor.X) + Math.Abs(p.Y - _pointerAnchor.Y) > MoveDismissPx)
                Dismiss();
        }

        private void OnRacingStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IRacingDetector.IsRunning)) return;
            if (!IRacingDetector.Shared.IsRunning) return;
            // Fires on the detector's poll thread — marshal the close to the UI thread.
            DispatcherQueue.TryEnqueue(Dismiss);
        }

        private void Dismiss()
        {
            if (_closed) return;
            Close(); // → OnClosed does the teardown
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            if (_closed) return;
            _closed = true;

            IRacingDetector.Shared.PropertyChanged -= OnRacingStateChanged;
            _rotateTimer?.Stop();
            _refreshTimer?.Stop();
            _rotateTimer = null;
            _refreshTimer = null;

            foreach (var tile in _tiles)
            {
                tile.Back.Source = null;
                tile.Front.Source = null;
            }
            _tiles.Clear();
            _pool.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Pick a source width tier (480 / 1280 / 2560) at least as large as the tile so
        /// downscaling — not upscaling — does the fitting. Decode is still capped to the
        /// tile size via BitmapImage.DecodePixelWidth.
        /// </summary>
        private static int NearestWidth(int px) => px <= 480 ? 480 : (px <= 1280 ? 1280 : 2560);

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private sealed class Tile
        {
            public required Grid Container { get; init; }
            public required Image Back { get; init; }
            public required Image Front { get; init; }
            public required TextBlock Credit { get; init; }
            public bool FrontVisible { get; set; }
        }
    }
}
