using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// GDI BitBlt capture of the primary display, shared by the ambient
    /// region picker (one full-res shot) and the Visual tab's layout
    /// editor (periodic scaled shots behind the proxy canvas).
    ///
    /// The pixel path goes GetDIBits → BitmapEncoder(PNG) → BitmapImage
    /// rather than poking WriteableBitmap.PixelBuffer: the
    /// IBufferByteAccess COM cast needed for the direct route can't be
    /// reliably obtained from a CsWinRT-projected IBuffer and silently
    /// produced an all-black image on real machines (the original
    /// AmbientRegionPicker bug). Don't "optimize" back to it.
    /// </summary>
    public static class ScreenCapture
    {
        /// <summary>
        /// Capture the primary display. When <paramref name="scaledWidth"/>
        /// is given, the encoder downscales before decode — use it for
        /// periodic refreshes so each tick moves a canvas-sized image,
        /// not a full-resolution one.
        /// </summary>
        public static async Task<BitmapImage> CaptureAsync(int width, int height, int? scaledWidth = null)
        {
            var pixelBytes = CaptureScreenBytes(width, height);

            var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            // Bgra8 + Ignore alpha: GDI 32bpp DIBs are BGRX (alpha byte
            // unused) — treat alpha as padding, not transparency.
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                (uint)width,
                (uint)height,
                96.0, 96.0,
                pixelBytes);
            if (scaledWidth is int sw && sw > 0 && sw < width)
            {
                encoder.BitmapTransform.ScaledWidth = (uint)sw;
                encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, (int)Math.Round(height * (double)sw / width));
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
            }
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
