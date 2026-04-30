using Windows.Storage;

namespace RaceCorProDrive.Support
{
    /// <summary>
    /// Dashboard filter prefs for Windows — mirrored from web /
    /// iOS / macOS / tvOS so the server's `?context=` and
    /// `?license=` URL parameters land the same shape on every
    /// platform.
    /// </summary>
    public enum DashboardContext
    {
        Race,
        Practice,
    }

    public enum DashboardLicense
    {
        All,
        Road,        // Display label is "Sports Car" (iRacing renamed
                     // road → Sports Car in 2024 S2). Internal code
                     // stays `road` so the wire format matches the
                     // server. Same convention codified in
                     // server-design-system skill.
        Oval,
        DirtRoad,
        DirtOval,
        Formula,
    }

    public static class DashboardContextExtensions
    {
        /// <summary>Wire value for the <c>?context=</c> query param.</summary>
        public static string ToWireValue(this DashboardContext ctx) => ctx switch
        {
            DashboardContext.Practice => "practice",
            _ => "race",
        };

        /// <summary>Plural display label ("Races" / "Practices").</summary>
        public static string DisplayName(this DashboardContext ctx) =>
            ctx.Noun(plural: true);

        /// <summary>
        /// Singular vs plural noun for race-vs-practice copy. Empty
        /// states ("No <c>noun</c> yet"), progress strings, and other
        /// human copy can render the right form regardless of context.
        /// </summary>
        public static string Noun(this DashboardContext ctx, bool plural)
        {
            return ctx switch
            {
                DashboardContext.Practice => plural ? "Practices" : "Practice",
                _ => plural ? "Races" : "Race",
            };
        }

        public static DashboardContext FromWireValue(string? wire) => wire switch
        {
            "practice" => DashboardContext.Practice,
            _ => DashboardContext.Race,
        };
    }

    public static class DashboardLicenseExtensions
    {
        /// <summary>Wire value for the <c>?license=</c> query param.</summary>
        public static string ToWireValue(this DashboardLicense lic) => lic switch
        {
            DashboardLicense.Road => "road",
            DashboardLicense.Oval => "oval",
            DashboardLicense.DirtRoad => "dirt_road",
            DashboardLicense.DirtOval => "dirt_oval",
            DashboardLicense.Formula => "formula",
            _ => "all",
        };

        /// <summary>Display label — "Sports Car" for Road per the convention.</summary>
        public static string DisplayName(this DashboardLicense lic) => lic switch
        {
            DashboardLicense.Road => "Sports Car",
            DashboardLicense.Oval => "Oval",
            DashboardLicense.DirtRoad => "Dirt Road",
            DashboardLicense.DirtOval => "Dirt Oval",
            DashboardLicense.Formula => "Formula",
            _ => "All Licenses",
        };

        public static DashboardLicense FromWireValue(string? wire) => wire switch
        {
            "road" => DashboardLicense.Road,
            "oval" => DashboardLicense.Oval,
            "dirt_road" => DashboardLicense.DirtRoad,
            "dirt_oval" => DashboardLicense.DirtOval,
            "formula" => DashboardLicense.Formula,
            _ => DashboardLicense.All,
        };
    }

    /// <summary>
    /// Persistent user prefs via Windows
    /// <see cref="ApplicationData.LocalSettings"/> — the WinUI 3
    /// equivalent of UserDefaults. Lives at
    /// <c>%LOCALAPPDATA%\Packages\&lt;package-id&gt;\LocalState</c>
    /// so it survives across app launches without us having to
    /// roll our own JSON file.
    ///
    /// Keys match iOS/macOS/tvOS so a future cross-device sync layer
    /// can map by string without inventing new identifiers.
    /// </summary>
    public static class DashboardFilterPrefs
    {
        public const string ContextKey = "dashboard.context";
        public const string LicenseKey = "dashboard.license";

        public static (DashboardContext Context, DashboardLicense License) Current()
        {
            // ApplicationData.Current is packaged-only; use the file-based
            // AppSettings store (lives under %LOCALAPPDATA%\RaceCorProDrive\)
            // that already works for unpackaged WinUI 3.
            return (
                DashboardContextExtensions.FromWireValue(AppSettings.Get(ContextKey)),
                DashboardLicenseExtensions.FromWireValue(AppSettings.Get(LicenseKey))
            );
        }

        public static void SetContext(DashboardContext ctx)
        {
            AppSettings.Set(ContextKey, ctx.ToWireValue());
        }

        public static void SetLicense(DashboardLicense lic)
        {
            AppSettings.Set(LicenseKey, lic.ToWireValue());
        }
    }
}
