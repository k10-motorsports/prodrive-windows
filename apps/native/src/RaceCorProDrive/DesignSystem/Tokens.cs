using Windows.UI;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Design system tokens for RaceCor Pro Drive.
    /// Matches the baseline from the web project's globals.css.
    /// </summary>
    public static class Tokens
    {
        // Brand colors
        public static class Colors
        {
            // Primary
            public static readonly Color K10Red = Color.FromArgb(255, 229, 57, 53); // #E53935
            public static readonly Color K10RedDark = Color.FromArgb(255, 200, 30, 30); // #C81E1E

            // Background
            public static readonly Color Background = Color.FromArgb(255, 10, 10, 20); // #0A0A14
            public static readonly Color SurfaceBase = Color.FromArgb(255, 26, 26, 36); // #1A1A24
            public static readonly Color SurfaceAlt = Color.FromArgb(255, 51, 51, 61); // #333333

            // Text
            public static readonly Color TextPrimary = Color.FromArgb(255, 255, 255, 255); // #FFFFFF
            public static readonly Color TextSecondary = Color.FromArgb(255, 160, 160, 160); // #A0A0A0
            public static readonly Color TextTertiary = Color.FromArgb(255, 112, 112, 112); // #707070

            // Status
            public static readonly Color ErrorRed = Color.FromArgb(255, 255, 107, 107); // #FF6B6B
            public static readonly Color WarningYellow = Color.FromArgb(255, 255, 192, 61); // #FFC03D
            public static readonly Color SuccessGreen = Color.FromArgb(255, 76, 175, 80); // #4CAF50
        }

        // Typography — system fonts (no licensed families shipped).
        // Segoe UI Variable ships with Windows 11; falls back to Segoe UI on 10.
        // Cascadia Code is preinstalled on Windows 11 and in Terminal on 10.
        public static class Typography
        {
            public const string DisplayFont = "Segoe UI Variable Display, Segoe UI";
            public const string BodyFont = "Segoe UI Variable Text, Segoe UI";
            public const string MonoFont = "Cascadia Code, Consolas";
        }

        // Spacing
        public static class Spacing
        {
            public const int XS = 4;
            public const int SM = 8;
            public const int MD = 12;
            public const int LG = 16;
            public const int XL = 24;
            public const int XXL = 32;
        }

        // Border radius
        public static class BorderRadius
        {
            public const int XS = 4;
            public const int SM = 6;
            public const int MD = 8;
            public const int LG = 12;
            public const int Full = 999;
        }
    }
}
