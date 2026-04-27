using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace RaceCorProDrive.Api
{
    // Mirrors the Swift `Dashboard` struct in the iOS / macOS / tvOS
    // builds 1:1 so web, server, and every native client agree on the
    // shape. New fields on the server flow through here without any
    // native re-computation — if it's not decoded, it's dropped.

    public class Dashboard
    {
        [JsonPropertyName("stats")]             public DashboardStatsFull Stats { get; set; } = new();
        [JsonPropertyName("pluginConnected")]   public bool PluginConnected { get; set; }

        [JsonPropertyName("iRating")]           public Dictionary<string, IRatingData>? IRating { get; set; }
        [JsonPropertyName("safetyRating")]      public Dictionary<string, SafetyRatingData>? SafetyRating { get; set; }

        [JsonPropertyName("moments")]           public List<MomentEntry>? Moments { get; set; }
        [JsonPropertyName("recentMoments")]     public List<MomentEntry>? RecentMoments { get; set; }

        [JsonPropertyName("topTracks")]         public List<TrackMasteryEntry>? TopTracks { get; set; }
        [JsonPropertyName("topCars")]           public List<CarAffinityEntry>? TopCars { get; set; }

        [JsonPropertyName("when")]              public WhenProfile? When { get; set; }
        [JsonPropertyName("whenPanel")]         public WhenPanelView? WhenPanel { get; set; }
        [JsonPropertyName("nextRaceIdeas")]     public List<NextRaceIdeaEntry>? NextRaceIdeas { get; set; }

        [JsonPropertyName("driverDNA")]         public DriverDNAData? DriverDNA { get; set; }
        [JsonPropertyName("driverArchetype")]   public DriverArchetypeData? DriverArchetype { get; set; }

        [JsonPropertyName("lookups")]           public DashboardLookups? Lookups { get; set; }
        [JsonPropertyName("scatterBuckets")]    public List<ScatterBucket>? ScatterBuckets { get; set; }
        [JsonPropertyName("recentSessions")]    public List<RecentSession>? RecentSessions { get; set; }

        // ── Convenience accessors (match the Swift extensions) ──

        public int TotalRaces    => Stats.TotalRaces;
        public int TotalTracks   => Stats.UniqueTracks;
        public int TotalCars     => Stats.UniqueCars;
        public int? CareerSpanDays => Stats.CareerSpanDays > 0 ? Stats.CareerSpanDays : null;

        /// Rating categories in display order, filtered to ones with a
        /// non-null current value. "road" is renamed to "Sports Car"
        /// — matches iRacing's ~2024 category rebrand and web parity.
        public IEnumerable<(string Key, string Label, IRatingData Rating)> ActiveCategories()
        {
            if (IRating == null) yield break;
            var order = new[] { "road", "oval", "formula", "dirt_road", "dirt_oval" };
            var labels = RaceCategoryLabels.Display;
            foreach (var key in order)
            {
                if (IRating.TryGetValue(key, out var r) && r?.Current > 0)
                {
                    yield return (key, labels.GetValueOrDefault(key, key), r);
                }
            }
        }
    }

    public class DashboardStatsFull
    {
        [JsonPropertyName("totalRaces")]       public int TotalRaces { get; set; }
        [JsonPropertyName("totalLaps")]        public int TotalLaps { get; set; }
        [JsonPropertyName("uniqueTracks")]     public int UniqueTracks { get; set; }
        [JsonPropertyName("uniqueCars")]       public int UniqueCars { get; set; }
        [JsonPropertyName("careerSpanDays")]   public int CareerSpanDays { get; set; }
    }

    public class IRatingData
    {
        [JsonPropertyName("current")]    public int? Current { get; set; }
        [JsonPropertyName("trend")]      public int Trend { get; set; }
        [JsonPropertyName("sparkline")]  public List<IRatingPoint> Sparkline { get; set; } = new();
    }

    public class IRatingPoint
    {
        [JsonPropertyName("t")]      public string T { get; set; } = "";
        [JsonPropertyName("value")]  public int Value { get; set; }
    }

    public class SafetyRatingData
    {
        [JsonPropertyName("current")]  public double? Current { get; set; }
        [JsonPropertyName("license")]  public string? License { get; set; }
    }

    public class MomentEntry
    {
        [JsonPropertyName("type")]          public string Type { get; set; } = "";
        [JsonPropertyName("date")]          public string Date { get; set; } = "";
        [JsonPropertyName("title")]         public string Title { get; set; } = "";
        [JsonPropertyName("description")]   public string Description { get; set; } = "";
        [JsonPropertyName("significance")]  public int Significance { get; set; }
        [JsonPropertyName("carModel")]      public string? CarModel { get; set; }
        [JsonPropertyName("trackName")]     public string? TrackName { get; set; }
        [JsonPropertyName("gameName")]      public string? GameName { get; set; }

        public string Id => $"{Type}-{Date}-{Title}";
    }

    public class TrackMasteryEntry
    {
        [JsonPropertyName("trackName")]      public string TrackName { get; set; } = "";
        [JsonPropertyName("familyKey")]      public string FamilyKey { get; set; } = "";
        [JsonPropertyName("totalSessions")]  public int TotalSessions { get; set; }
        [JsonPropertyName("totalLaps")]      public int TotalLaps { get; set; }
        [JsonPropertyName("avgPosition")]    public double? AvgPosition { get; set; }
        [JsonPropertyName("bestPosition")]   public int? BestPosition { get; set; }
        [JsonPropertyName("avgIncidents")]   public double AvgIncidents { get; set; }
        [JsonPropertyName("masteryScore")]   public double MasteryScore { get; set; }
        [JsonPropertyName("masteryTier")]    public string MasteryTier { get; set; } = "bronze";
        [JsonPropertyName("trend")]          public string Trend { get; set; } = "stable";
        [JsonPropertyName("lastRaced")]      public string LastRaced { get; set; } = "";
    }

    public class CarAffinityEntry
    {
        [JsonPropertyName("manufacturer")]  public string Manufacturer { get; set; } = "";
        [JsonPropertyName("brandKey")]      public string BrandKey { get; set; } = "";
        [JsonPropertyName("cars")]          public List<CarEntry>? Cars { get; set; }
        [JsonPropertyName("totalSessions")] public int TotalSessions { get; set; }
        [JsonPropertyName("totalLaps")]     public int TotalLaps { get; set; }
        [JsonPropertyName("avgPosition")]   public double? AvgPosition { get; set; }
        [JsonPropertyName("bestPosition")]  public int? BestPosition { get; set; }
        [JsonPropertyName("avgIncidents")]  public double AvgIncidents { get; set; }
        [JsonPropertyName("affinityScore")] public double AffinityScore { get; set; }
        [JsonPropertyName("trend")]         public string Trend { get; set; } = "stable";
    }

    public class CarEntry
    {
        [JsonPropertyName("carModel")]      public string CarModel { get; set; } = "";
        [JsonPropertyName("gameName")]      public string GameName { get; set; } = "";
        [JsonPropertyName("sessionCount")]  public int SessionCount { get; set; }
    }

    public class TemporalSlice
    {
        [JsonPropertyName("label")]            public string Label { get; set; } = "";
        [JsonPropertyName("sessionCount")]     public int SessionCount { get; set; }
        [JsonPropertyName("avgPosition")]      public double? AvgPosition { get; set; }
        [JsonPropertyName("avgIRatingDelta")]  public double? AvgIRatingDelta { get; set; }
        [JsonPropertyName("avgIncidents")]     public double AvgIncidents { get; set; }
        [JsonPropertyName("winRate")]          public double WinRate { get; set; }
        [JsonPropertyName("podiumRate")]       public double PodiumRate { get; set; }
        [JsonPropertyName("topTenRate")]       public double TopTenRate { get; set; }
    }

    public class WhenInsight
    {
        [JsonPropertyName("type")]  public string Type { get; set; } = "";
        [JsonPropertyName("text")]  public string Text { get; set; } = "";
    }

    public class HeatmapCell
    {
        [JsonPropertyName("day")]    public int Day { get; set; }
        [JsonPropertyName("hour")]   public int Hour { get; set; }
        [JsonPropertyName("score")]  public double Score { get; set; }
        [JsonPropertyName("count")]  public int Count { get; set; }
    }

    public class WhenProfile
    {
        [JsonPropertyName("byHour")]            public List<TemporalSlice> ByHour { get; set; } = new();
        [JsonPropertyName("byDayOfWeek")]       public List<TemporalSlice> ByDayOfWeek { get; set; } = new();
        [JsonPropertyName("bySessionLength")]   public List<TemporalSlice> BySessionLength { get; set; } = new();
        [JsonPropertyName("peakHours")]         public string PeakHours { get; set; } = "";
        [JsonPropertyName("peakDays")]          public string PeakDays { get; set; } = "";
        [JsonPropertyName("worstHours")]        public string WorstHours { get; set; } = "";
        [JsonPropertyName("worstDays")]         public string WorstDays { get; set; } = "";
        [JsonPropertyName("peakHourStart")]     public int PeakHourStart { get; set; }
        [JsonPropertyName("worstHourStart")]    public int WorstHourStart { get; set; }
        [JsonPropertyName("windowSize")]        public int WindowSize { get; set; }
        [JsonPropertyName("insights")]          public List<WhenInsight> Insights { get; set; } = new();
        [JsonPropertyName("heatmapData")]       public List<HeatmapCell> HeatmapData { get; set; } = new();
        // Quantile-ranked tier for every hour 0–23. The Race-Now calc
        // engine reads `rankedSlots[currentHour].tier` to produce the
        // six-tier verdict; without this field on the typed class, the
        // JSON round-trip in DashboardPage.RefreshRaceNowAsync (typed
        // WhenProfile → JsonSerializer.Serialize → JsonDocument.Parse →
        // re-send to /calc/race-now) drops the property and the server
        // falls back to "insufficient". That's the divergence where
        // native clients read "Not enough data yet" while the web app
        // showed "Cleaner half" for the same hour.
        [JsonPropertyName("rankedSlots")]       public List<RankedSlot> RankedSlots { get; set; } = new();
    }

    /// <summary>
    /// One row of the server's <c>WhenProfile.rankedSlots</c> table — the
    /// quantile rank of an hour-of-week against this driver's own
    /// distribution. The calc engine reads <c>tier</c> to pick a verdict;
    /// the rest of the fields are kept so the round-trip is lossless.
    /// </summary>
    public class RankedSlot
    {
        [JsonPropertyName("hour")]          public int Hour { get; set; }
        [JsonPropertyName("rank")]          public int? Rank { get; set; }
        [JsonPropertyName("percentile")]    public double? Percentile { get; set; }
        [JsonPropertyName("tier")]          public string Tier { get; set; } = "";
        [JsonPropertyName("sessionCount")]  public int SessionCount { get; set; }
        [JsonPropertyName("avgIncidents")]  public double AvgIncidents { get; set; }
        [JsonPropertyName("avgPosition")]   public double? AvgPosition { get; set; }
    }

    public class NextRaceIdeaEntry
    {
        [JsonPropertyName("seriesName")]     public string SeriesName { get; set; } = "";
        [JsonPropertyName("trackName")]      public string TrackName { get; set; } = "";
        [JsonPropertyName("trackConfig")]    public string? TrackConfig { get; set; }
        [JsonPropertyName("category")]       public string Category { get; set; } = "";
        [JsonPropertyName("license")]        public string License { get; set; } = "";
        [JsonPropertyName("official")]       public bool Official { get; set; }
        [JsonPropertyName("fixed")]          public bool Fixed { get; set; }
        [JsonPropertyName("score")]          public double Score { get; set; }
        [JsonPropertyName("strategy")]       public string Strategy { get; set; } = "";
        [JsonPropertyName("commentary")]     public string Commentary { get; set; } = "";
        [JsonPropertyName("startsAtUtc")]    public string StartsAtUtc { get; set; } = "";
        [JsonPropertyName("carClassNames")]  public List<string> CarClassNames { get; set; } = new();
        [JsonPropertyName("seasonId")]       public int SeasonId { get; set; }
        [JsonPropertyName("seriesId")]       public int SeriesId { get; set; }
        [JsonPropertyName("raceLapLimit")]   public int? RaceLapLimit { get; set; }
        [JsonPropertyName("raceTimeLimit")]  public int? RaceTimeLimit { get; set; }
    }

    public class WhenPanelSide
    {
        [JsonPropertyName("days")]              public string Days { get; set; } = "";
        [JsonPropertyName("hours")]             public string Hours { get; set; } = "";
        [JsonPropertyName("avgIRatingDelta")]   public double? AvgIRatingDelta { get; set; }
        [JsonPropertyName("paragraph")]         public string Paragraph { get; set; } = "";
        [JsonPropertyName("bullets")]           public List<WhenInsight> Bullets { get; set; } = new();
    }

    public class WhenPanelView
    {
        [JsonPropertyName("strengths")]  public WhenPanelSide? Strengths { get; set; }
        [JsonPropertyName("watchOut")]   public WhenPanelSide? WatchOut { get; set; }
    }

    public class DriverDNAData
    {
        [JsonPropertyName("consistency")]  public double Consistency { get; set; }
        [JsonPropertyName("racecraft")]    public double Racecraft { get; set; }
        [JsonPropertyName("cleanness")]    public double Cleanness { get; set; }
        [JsonPropertyName("endurance")]    public double Endurance { get; set; }
        [JsonPropertyName("adaptability")] public double Adaptability { get; set; }
        [JsonPropertyName("improvement")]  public double Improvement { get; set; }
        [JsonPropertyName("wetWeather")]   public double WetWeather { get; set; }
        [JsonPropertyName("experience")]   public double Experience { get; set; }
    }

    public class DriverArchetypeData
    {
        [JsonPropertyName("major")]                public string Major { get; set; } = "";
        [JsonPropertyName("variant")]              public string Variant { get; set; } = "";
        [JsonPropertyName("majorDescription")]     public string? MajorDescription { get; set; }
        [JsonPropertyName("variantDescription")]   public string? VariantDescription { get; set; }
    }

    public class TrackLookup
    {
        [JsonPropertyName("key")]           public string Key { get; set; } = "";
        [JsonPropertyName("displayName")]   public string DisplayName { get; set; } = "";
        [JsonPropertyName("svgPath")]       public string? SvgPath { get; set; }
        [JsonPropertyName("logoSvg")]       public string? LogoSvg { get; set; }
        [JsonPropertyName("imageUrl")]      public string? ImageUrl { get; set; }
    }

    public class CarLookup
    {
        [JsonPropertyName("key")]           public string Key { get; set; } = "";
        [JsonPropertyName("brandKey")]      public string? BrandKey { get; set; }
        [JsonPropertyName("manufacturer")]  public string? Manufacturer { get; set; }
        [JsonPropertyName("logoSvg")]       public string? LogoSvg { get; set; }
        [JsonPropertyName("logoPng")]       public string? LogoPng { get; set; }
        [JsonPropertyName("color")]         public string? Color { get; set; }
        [JsonPropertyName("imageUrl")]      public string? ImageUrl { get; set; }
    }

    public class DashboardLookups
    {
        [JsonPropertyName("tracks")]  public List<TrackLookup> Tracks { get; set; } = new();
        [JsonPropertyName("cars")]    public List<CarLookup> Cars { get; set; } = new();

        public TrackLookup? TrackFor(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var key = name.Trim().ToLowerInvariant();
            return Tracks.FirstOrDefault(t => t.Key == key);
        }

        public CarLookup? CarFor(string? model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            var key = model.Trim().ToLowerInvariant();
            return Cars.FirstOrDefault(c => c.Key == key)
                ?? Cars.FirstOrDefault(c => !string.IsNullOrEmpty(c.BrandKey)
                                            && key.Contains(c.BrandKey!));
        }
    }

    public class ScatterBucket
    {
        [JsonPropertyName("day")]             public int Day { get; set; }
        [JsonPropertyName("hour")]            public int Hour { get; set; }
        [JsonPropertyName("count")]           public int Count { get; set; }
        [JsonPropertyName("irDeltaSum")]      public double IrDeltaSum { get; set; }
        [JsonPropertyName("srDeltaSum")]      public double SrDeltaSum { get; set; }
        [JsonPropertyName("incidentsSum")]    public double IncidentsSum { get; set; }
        [JsonPropertyName("avgIncidents")]    public double AvgIncidents { get; set; }
    }

    public class RecentSession
    {
        [JsonPropertyName("id")]              public string Id { get; set; } = "";
        [JsonPropertyName("subsessionId")]    public int SubsessionId { get; set; }
        [JsonPropertyName("seriesName")]      public string SeriesName { get; set; } = "";
        [JsonPropertyName("trackName")]       public string TrackName { get; set; } = "";
        [JsonPropertyName("startTime")]       public DateTime StartTime { get; set; }
        [JsonPropertyName("duration")]        public int? Duration { get; set; }
        [JsonPropertyName("finishPosition")]  public int? FinishPosition { get; set; }
        [JsonPropertyName("lapsCompleted")]   public int? LapsCompleted { get; set; }
        [JsonPropertyName("category")]        public string Category { get; set; } = "road";
        [JsonPropertyName("carModel")]        public string? CarModel { get; set; }
        [JsonPropertyName("incidentCount")]   public int? IncidentCount { get; set; }
    }

    /// Shared map of internal category keys → user-facing display
    /// names. Canonical source: `apps/web/src/lib/constants.ts` on
    /// the server. Copy shows "Sports Car" for legacy "road" rows.
    public static class RaceCategoryLabels
    {
        public static readonly Dictionary<string, string> Display = new()
        {
            ["road"]      = "Sports Car",
            ["oval"]      = "Oval",
            ["formula"]   = "Formula",
            ["dirt_road"] = "Dirt Road",
            ["dirt_oval"] = "Dirt Oval",
        };
    }

    /// `GET /api/v1/me` shape. Server fields are all nullable except
    /// id/discordId; createdAt is kept as a DateTime because C# has
    /// no reason to stringify it.
    public class Me
    {
        [JsonPropertyName("id")]                   public string Id { get; set; } = "";
        [JsonPropertyName("discordId")]            public string DiscordId { get; set; } = "";
        [JsonPropertyName("discordUsername")]      public string? DiscordUsername { get; set; }
        [JsonPropertyName("discordDisplayName")]   public string? DiscordDisplayName { get; set; }
        [JsonPropertyName("discordAvatar")]        public string? DiscordAvatar { get; set; }
        [JsonPropertyName("customLogoUrl")]        public string? CustomLogoUrl { get; set; }
        [JsonPropertyName("email")]                public string? Email { get; set; }
        [JsonPropertyName("createdAt")]            public DateTime CreatedAt { get; set; }

        public string DisplayName => DiscordDisplayName ?? DiscordUsername ?? Email ?? "Racer";
    }
}
