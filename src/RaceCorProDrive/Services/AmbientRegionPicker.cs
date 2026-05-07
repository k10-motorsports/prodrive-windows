using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Fullscreen "drag to pick" overlay used by the Settings page to set
    /// the screen region the C# SimHub plugin's ScreenColorSampler reads
    /// for ambient lighting. Captures the primary display via GDI
    /// BitBlt, renders the screenshot dimmed under a draggable
    /// rectangle, and resolves an <see cref="AmbientRect"/> in 0..1
    /// ratios (or <c>null</c> if the user cancels).
    ///
    /// The overlay's ambient-capture.js consumes the same JSON shape, so
    /// the rect saved by this picker is consumed live: the host writes
    /// to overlay-settings.json, the overlay's settings watcher fires,
    /// applySettings() calls restoreAmbientCapture(), and the rect is
    /// POSTed to the plugin's setrect endpoint. No host↔overlay IPC.
    /// </summary>
    public static class AmbientRegionPicker
    {
        public static async Task<AmbientRect?> PickAsync()
        {
            var tcs = new TaskCompletionSource<AmbientRect?>();

            var primary = DisplayArea.Primary;
            var bounds = primary.OuterBounds;

            // Capture the screen FIRST (before our window is on screen)
            // so we don't end up screenshotting our own dim layer.
            // BitmapImage path is more reliable across CsWinRT runtimes
            // than poking the WriteableBitmap pixel buffer directly via
            // IBufferByteAccess COM interop, which silently produced an
            // empty buffer on some setups (the user saw "all black").
            BitmapImage? screenshot = null;
            try
            {
                screenshot = await CaptureScreenAsync(bounds.Width, bounds.Height);
            }
            catch
            {
                // Capture failed — picker still works, the user will
                // just see a black backdrop instead of their screen.
                screenshot = null;
            }

            var pickerWindow = new Window();
            pickerWindow.Title = "Pick ambient capture region";

            var hwnd = WindowNative.GetWindowHandle(pickerWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }
            appWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));

            // ── Layer stack ────────────────────────────────────────
            // Grid:
            //   [0] Image — captured screenshot, full-bleed
            //   [1] Border — semi-transparent dim
            //   [2] Canvas — receives pointer events + draws the rect
            //   [3] Border — instruction strip pinned to top
            // The Grid is wrapped in a ContentControl so KeyDown +
            // Focus() are available (those live on Control, not on
            // FrameworkElement) — that's how Esc-to-cancel works.
            var root = new Grid();

            if (screenshot != null)
            {
                var img = new Image
                {
                    Source = screenshot,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                root.Children.Add(img);
            }
            else
            {
                root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x10, 0x10, 0x10));
            }

            // Light dim so the screenshot is still legible; before this
            // tweak the overlay was dark enough that the user couldn't
            // see what they were selecting.
            var dim = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            };
            root.Children.Add(dim);

            var canvas = new Canvas
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
                IsHitTestVisible = true,
            };
            var rect = new Rectangle
            {
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xC8, 0x3C)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xC8, 0x3C)),
                Visibility = Visibility.Collapsed,
            };
            canvas.Children.Add(rect);
            root.Children.Add(canvas);

            var instruction = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
                Padding = new Thickness(16, 10, 16, 10),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "Drag to set capture region · Esc to cancel",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                    FontSize = 14,
                },
            };
            root.Children.Add(instruction);

            var rootHost = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = root,
                IsTabStop = true,
            };
            pickerWindow.Content = rootHost;

            // ── Pointer-driven rectangle drawing ───────────────────
            bool dragging = false;
            double startX = 0, startY = 0;
            double curX = 0, curY = 0;

            canvas.PointerPressed += (s, e) =>
            {
                var p = e.GetCurrentPoint(canvas);
                if (!p.Properties.IsLeftButtonPressed) return;
                dragging = true;
                startX = p.Position.X;
                startY = p.Position.Y;
                curX = startX;
                curY = startY;
                Canvas.SetLeft(rect, startX);
                Canvas.SetTop(rect, startY);
                rect.Width = 0;
                rect.Height = 0;
                rect.Visibility = Visibility.Visible;
                canvas.CapturePointer(e.Pointer);
            };

            canvas.PointerMoved += (s, e) =>
            {
                if (!dragging) return;
                var p = e.GetCurrentPoint(canvas);
                curX = p.Position.X;
                curY = p.Position.Y;
                var x = Math.Min(startX, curX);
                var y = Math.Min(startY, curY);
                var w = Math.Abs(curX - startX);
                var h = Math.Abs(curY - startY);
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                rect.Width = w;
                rect.Height = h;
            };

            canvas.PointerReleased += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                canvas.ReleasePointerCapture(e.Pointer);

                var x = Math.Min(startX, curX);
                var y = Math.Min(startY, curY);
                var w = Math.Abs(curX - startX);
                var h = Math.Abs(curY - startY);

                // Throwaway clicks (no real drag) — treat as cancel.
                if (w < 12 || h < 12)
                {
                    rect.Visibility = Visibility.Collapsed;
                    return;
                }

                var actualW = canvas.ActualWidth > 0 ? canvas.ActualWidth : bounds.Width;
                var actualH = canvas.ActualHeight > 0 ? canvas.ActualHeight : bounds.Height;
                var picked = new AmbientRect
                {
                    X = x / actualW,
                    Y = y / actualH,
                    W = w / actualW,
                    H = h / actualH,
                };

                tcs.TrySetResult(picked);
                pickerWindow.Close();
            };

            // ── Esc to cancel ──────────────────────────────────────
            // Wire on the host so the user doesn't need to focus a
            // specific element. The Grid's KeyDown bubbles up to the
            // ContentControl, but attaching here is most direct.
            rootHost.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    tcs.TrySetResult(null);
                    pickerWindow.Close();
                }
            };

            pickerWindow.Closed += (_, __) =>
            {
                // Belt-and-suspenders: if the window closes without
                // resolving (e.g. the user clicks the system close
                // affordance somehow), treat it as cancel.
                tcs.TrySetResult(null);
            };

            pickerWindow.Activate();
            // Focus the host so KeyDown for Esc fires immediately
            // without requiring the user to click first.
            rootHost.Focus(FocusState.Programmatic);

            return tcs.Task;
        }

        // ── Win32 GDI screen capture ───────────────────────────────
        // Uses BitBlt against the desktop DC, then encodes the captured
        // bytes as PNG into an in-memory stream and decodes them as a
        // BitmapImage. Goes through BitmapEncoder rather than poking
        // WriteableBitmap.PixelBuffer because the latter requires an
        // IBufferByteAccess COM cast that can't be reliably obtained
        // from a CsWinRT-projected IBuffer — that path silently
        // produced an empty buffer on the user's machine, leading to
        // an all-black region picker.

        private static async Task<BitmapImage> CaptureScreenAsync(int width, int height)
        {
            var pixelBytes = CaptureScreenBytes(width, height);

            var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            // Bgra8 + Ignore alpha: GDI 32bpp DIBs are BGRX (alpha byte
            // unused), and PNG's alpha channel doesn't make sense for
            // an opaque desktop screenshot. Ignore tells the encoder to
            // treat alpha bytes as padding rather than transparency.
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                (uint)width,
                (uint)height,
                96.0, 96.0,
                pixelBytes);
            await encoder.FlushAsync();
            stream.Seek(0);

            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            return bmp;
        }

        private static byte[] CaptureScreenBytes(int width, int height)
        {
            var desktopDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(desktopDc);
            var bmp = CreateCompatibleBitmap(desktopDc, width, height);
            var oldBmp = SelectObject(memDc, bmp);

            try
            {
                BitBlt(memDc, 0, 0, width, height, desktopDc, 0, 0, SRCCOPY | CAPTUREBLT);

                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // negative = top-down DIB
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0, // BI_RGB
                    },
                };

                var pixelBytes = new byte[width * height * 4];
                GetDIBits(memDc, bmp, 0, (uint)height, pixelBytes, ref bmi, 0);
                return pixelBytes;
            }
            finally
            {
                SelectObject(memDc, oldBmp);
                DeleteObject(bmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, desktopDc);
            }
        }

        // ── P/Invoke ───────────────────────────────────────────────

        private const int SRCCOPY    = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool   DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] private static extern bool   DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(
            IntPtr hdc, int x, int y, int cx, int cy,
            IntPtr hdcSrc, int x1, int y1, int rop);
        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(
            IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
            [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint usage);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int  biWidth;
            public int  biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int  biXPelsPerMeter;
            public int  biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            // bmiColors[1] not needed for 32bpp — biCompression=0 + 32bpp
            // means the pixel format is fixed and the palette is unused.
        }
    }
}
