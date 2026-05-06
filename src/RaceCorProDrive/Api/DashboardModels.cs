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
        [JsonPropertyName("previousRaces")]     public List<PreviousRaceCard>? PreviousRaces { get; set; }

        // ── Pit Wall metrics ──
        // The four load-bearing platform metrics defined in
        // `agents/prodrive-context/glossary.md`:
        //   Composure  — rolling iR delta per incident-point.
        //   Heat       — short-window form indicator.
        //   Streaks    — current run + longest slump / surge.
        //   Discipline — per-category mix with per-discipline Composure.
        // Vocabulary rule: every member name and any string the user can
        // see uses racing-native terms. No finance vocabulary anywhere.
        [JsonPropertyName("composure")]        public ComposureResult? Composure { get; set; }
        [JsonPropertyName("heat")]             public HeatResult? Heat { get; set; }
        [JsonPropertyName("streaks")]          public StreaksResult? Streaks { get; set; }
        [JsonPropertyName("disciplineMix")]    public DisciplineMixResult? DisciplineMix { get; set; }
        [JsonPropertyName("composureSeries")]  public ComposureSeriesResult? ComposureSeries { get; set; }
        [JsonPropertyName("trajectory")]       public TrajectoryResult? Trajectory { get; set; }

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
        // Server-baked presentation strings + colors. Nullable so old
        // server responses (pre-prodrive-server PR #31) deserialize
        // without error; renderer falls back to local formatters when
        // null. Once server is deployed, every response populates this
        // and clients render verbatim with no drift across web /
        // macOS / iOS / tvOS / Windows.
        [JsonPropertyName("viewModel")]      public NextRaceIdeaViewModel? ViewModel { get; set; }
    }

    /// <summary>
    /// Mirrors <c>RaceSuggestion.viewModel</c> on the server side
    /// (<c>apps/web-api/src/calc/next-race-ideas.ts</c>). All clients
    /// render these strings verbatim — no client-side score-color
    /// thresholding, no client-side countdown formatting.
    /// </summary>
    public class NextRaceIdeaViewModel
    {
        [JsonPropertyName("scoreColorHex")]    public string ScoreColorHex { get; set; } = "";
        [JsonPropertyName("scoreTierLabel")]   public string ScoreTierLabel { get; set; } = "";
        [JsonPropertyName("startsInLabel")]    public string StartsInLabel { get; set; } = "";
        [JsonPropertyName("durationLabel")]    public string? DurationLabel { get; set; }
        [JsonPropertyName("strategyLabel")]    public string StrategyLabel { get; set; } = "";
        [JsonPropertyName("strategyColorHex")] public string StrategyColorHex { get; set; } = "";
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

    /// One session bundled into a Previous Races card. Mirrors the
    /// shape produced by `apps/web-api/src/calc/previous-races.ts`.
    public class PreviousRaceSession
    {
        [JsonPropertyName("id")]              public string Id { get; set; } = "";
        [JsonPropertyName("carModel")]        public string CarModel { get; set; } = "";
        [JsonPropertyName("manufacturer")]    public string? Manufacturer { get; set; }
        [JsonPropertyName("trackName")]       public string? TrackName { get; set; }
        [JsonPropertyName("trackConfig")]     public string? TrackConfig { get; set; }
        [JsonPropertyName("finishPosition")]  public int? FinishPosition { get; set; }
        [JsonPropertyName("incidentCount")]   public int? IncidentCount { get; set; }
        [JsonPropertyName("bestLapTime")]     public double? BestLapTime { get; set; }
        [JsonPropertyName("fieldSize")]       public int? FieldSize { get; set; }
        [JsonPropertyName("completedLaps")]   public int? CompletedLaps { get; set; }
        [JsonPropertyName("gameName")]        public string? GameName { get; set; }
        [JsonPropertyName("sessionType")]     public string? SessionType { get; set; }
        [JsonPropertyName("category")]        public string Category { get; set; } = "";
        [JsonPropertyName("createdAt")]       public string CreatedAt { get; set; } = "";
    }

    /// Server-baked presentation strings for a Previous Races card.
    /// Native renders these verbatim — see the SwiftUI counterpart.
    public class PreviousRaceViewModel
    {
        [JsonPropertyName("sessionTypeTab")]            public string SessionTypeTab { get; set; } = "races";
        [JsonPropertyName("positionDisplay")]           public string? PositionDisplay { get; set; }
        [JsonPropertyName("positionColorHex")]          public string? PositionColorHex { get; set; }
        [JsonPropertyName("fieldSizeLabel")]            public string? FieldSizeLabel { get; set; }
        [JsonPropertyName("bestLapLabel")]              public string BestLapLabel { get; set; } = "—";
        [JsonPropertyName("practiceBestLapLabel")]      public string? PracticeBestLapLabel { get; set; }
        [JsonPropertyName("qualifyingBestLapLabel")]    public string? QualifyingBestLapLabel { get; set; }
        [JsonPropertyName("qualifyingPositionLabel")]   public string? QualifyingPositionLabel { get; set; }
        [JsonPropertyName("dateLabel")]                 public string DateLabel { get; set; } = "";
        [JsonPropertyName("sessionLabel")]              public string SessionLabel { get; set; } = "";
        [JsonPropertyName("incidentsLabel")]            public string? IncidentsLabel { get; set; }
        [JsonPropertyName("practiceIncidentsLabel")]    public string? PracticeIncidentsLabel { get; set; }
        [JsonPropertyName("gameLabel")]                 public string GameLabel { get; set; } = "";
        [JsonPropertyName("trackLabel")]                public string TrackLabel { get; set; } = "";
    }

    /// One Previous Races card — already paired with sibling practice
    /// and qualifying sessions on the server side.
    public class PreviousRaceCard
    {
        [JsonPropertyName("session")]            public PreviousRaceSession Session { get; set; } = new();
        [JsonPropertyName("practiceSession")]    public PreviousRaceSession? PracticeSession { get; set; }
        [JsonPropertyName("qualifyingSession")]  public PreviousRaceSession? QualifyingSession { get; set; }
        [JsonPropertyName("viewModel")]          public PreviousRaceViewModel ViewModel { get; set; } = new();

        public string Id => Session.Id;
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

    // ── Pit Wall metrics ──────────────────────────────────────────────────
    // Wire shape mirrors `apps/web-api/src/calc/composure.ts`. Every native
    // client (Swift / C#) carries the same fields verbatim.

    public class ComposureResult
    {
        [JsonPropertyName("sampleSize")]          public int SampleSize { get; set; }
        [JsonPropertyName("score")]               public int Score { get; set; }
        /// "gritty" | "steady" | "sharp" | "untouchable"
        [JsonPropertyName("band")]                public string Band { get; set; } = "steady";
        [JsonPropertyName("iRatingDeltaSum")]     public int IRatingDeltaSum { get; set; }
        [JsonPropertyName("incidentSum")]         public int IncidentSum { get; set; }
        [JsonPropertyName("avgIncidents")]        public double AvgIncidents { get; set; }
        [JsonPropertyName("iRatingPerIncident")]  public double? IRatingPerIncident { get; set; }
        /// "improving" | "declining" | "stable" | "new"
        [JsonPropertyName("trend")]               public string Trend { get; set; } = "new";
    }

    public class HeatResult
    {
        [JsonPropertyName("sampleSize")]          public int SampleSize { get; set; }
        [JsonPropertyName("iRatingDeltaSum")]     public int IRatingDeltaSum { get; set; }
        [JsonPropertyName("iRatingDeltaAvg")]     public double IRatingDeltaAvg { get; set; }
        [JsonPropertyName("avgIncidents")]        public double AvgIncidents { get; set; }
        /// "hot" | "warm" | "flat" | "cold"
        [JsonPropertyName("direction")]           public string Direction { get; set; } = "flat";
    }

    public class StreakSpan
    {
        [JsonPropertyName("length")]              public int Length { get; set; }
        [JsonPropertyName("startDate")]           public string StartDate { get; set; } = "";
        [JsonPropertyName("endDate")]             public string EndDate { get; set; } = "";
        [JsonPropertyName("iRatingDeltaSum")]     public int IRatingDeltaSum { get; set; }
        [JsonPropertyName("incidentSum")]         public int IncidentSum { get; set; }
    }

    public class CurrentStreak
    {
        /// "gaining" | "losing" | "flat"
        [JsonPropertyName("kind")]                public string Kind { get; set; } = "flat";
        [JsonPropertyName("span")]                public StreakSpan Span { get; set; } = new();
    }

    public class StreaksResult
    {
        [JsonPropertyName("current")]             public CurrentStreak? Current { get; set; }
        [JsonPropertyName("longestSlump")]        public StreakSpan? LongestSlump { get; set; }
        [JsonPropertyName("longestSurge")]        public StreakSpan? LongestSurge { get; set; }
    }

    public class DisciplineSlice
    {
        [JsonPropertyName("category")]            public string Category { get; set; } = "";
        [JsonPropertyName("count")]               public int Count { get; set; }
        [JsonPropertyName("proportion")]          public double Proportion { get; set; }
        [JsonPropertyName("iRatingDeltaSum")]     public int IRatingDeltaSum { get; set; }
        [JsonPropertyName("incidentSum")]         public int IncidentSum { get; set; }
        [JsonPropertyName("avgIncidents")]        public double AvgIncidents { get; set; }
        [JsonPropertyName("composure")]           public ComposureResult Composure { get; set; } = new();
    }

    public class DisciplineMixResult
    {
        [JsonPropertyName("total")]               public int Total { get; set; }
        [JsonPropertyName("byCategory")]          public List<DisciplineSlice> ByCategory { get; set; } = new();
        [JsonPropertyName("primary")]             public string? Primary { get; set; }
        [JsonPropertyName("bestForRating")]       public string? BestForRating { get; set; }
    }

    public class ComposureSeriesPoint
    {
        [JsonPropertyName("date")]                public string Date { get; set; } = "";
        [JsonPropertyName("score")]               public int Score { get; set; }
        [JsonPropertyName("band")]                public string Band { get; set; } = "steady";
        [JsonPropertyName("sampleSize")]          public int SampleSize { get; set; }
    }

    public class ComposureSeriesResult
    {
        [JsonPropertyName("points")]              public List<ComposureSeriesPoint> Points { get; set; } = new();
        [JsonPropertyName("minScore")]            public int MinScore { get; set; }
        [JsonPropertyName("maxScore")]            public int MaxScore { get; set; } = 100;
    }

    public class TrajectoryHorizon
    {
        [JsonPropertyName("days")]                public int Days { get; set; }
        [JsonPropertyName("expectedDelta")]       public int ExpectedDelta { get; set; }
        [JsonPropertyName("variance")]            public int Variance { get; set; }
    }

    public class TrajectoryResult
    {
        [JsonPropertyName("iRatingPerRace")]      public double IRatingPerRace { get; set; }
        [JsonPropertyName("racesPerWeek")]        public double RacesPerWeek { get; set; }
        [JsonPropertyName("stdDevPerRace")]       public double StdDevPerRace { get; set; }
        /// "climbing" | "falling" | "plateau"
        [JsonPropertyName("direction")]           public string Direction { get; set; } = "plateau";
        [JsonPropertyName("sampleSize")]          public int SampleSize { get; set; }
        [JsonPropertyName("horizons")]            public List<TrajectoryHorizon> Horizons { get; set; } = new();
    }
}
