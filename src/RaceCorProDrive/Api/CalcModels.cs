using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceCorProDrive.Api
{
    // ───────────────────────────────────────────────────────────────
    //  DTOs for /api/v1/calc/* requests and responses.
    //
    //  Spec: prodrive-server/docs/native-app-integration.md
    //
    //  Conventions:
    //  - All `createdAt` / time fields are ISO-8601 strings on the wire;
    //    System.Text.Json's default DateTime handling reads them as DateTime.
    //  - Heavily nested or evolving response shapes (TrackMastery,
    //    WhenProfile, ComposureReport, IRacingSchedule, ScoreBreakdown)
    //    are kept as JsonElement so additive server changes don't break
    //    existing clients. Cast at the call site if you need it.
    // ───────────────────────────────────────────────────────────────

    // ─── 1. Driver DNA ───────────────────────────────────────────────

    public sealed class DnaSession
    {
        public int? FinishPosition { get; set; }
        public int? IncidentCount { get; set; }
        public JsonElement? Metadata { get; set; }
        public string CarModel { get; set; } = "";
        public string? TrackName { get; set; }
        public string? GameName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class DnaRating
    {
        public double IRating { get; set; }
        public double? PrevIRating { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class DriverDnaResponse
    {
        public DriverDnaScores Dna { get; set; } = new();
        public DriverDnaArchetype Archetype { get; set; } = new();
        public List<DriverDnaInsight> Insights { get; set; } = new();
        public int SampleSize { get; set; }
        public double Confidence { get; set; }
    }

    public sealed class DriverDnaScores
    {
        public double Consistency { get; set; }
        public double Racecraft { get; set; }
        public double Cleanness { get; set; }
        public double Endurance { get; set; }
        public double Adaptability { get; set; }
        public double Improvement { get; set; }
        public double WetWeather { get; set; }
        public double Experience { get; set; }
    }

    public sealed class DriverDnaArchetype
    {
        public string Major { get; set; } = "";
        public string Variant { get; set; } = "";
        public string MajorDescription { get; set; } = "";
        public string VariantDescription { get; set; } = "";
    }

    public sealed class DriverDnaInsight
    {
        public string Dimension { get; set; } = "";
        public string Label { get; set; } = "";
        public double Value { get; set; }
        public string Description { get; set; } = "";
        public string Trend { get; set; } = ""; // "improving" | "declining" | "stable"
    }

    // ─── 2. Mastery ──────────────────────────────────────────────────

    public sealed class MasterySession
    {
        public string Id { get; set; } = "";
        public string CarModel { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string TrackName { get; set; } = "";
        public int? FinishPosition { get; set; }
        public int IncidentCount { get; set; }
        public JsonElement? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
        public string GameName { get; set; } = "iracing";
    }

    public sealed class MasteryResponse
    {
        public List<JsonElement> Tracks { get; set; } = new();
        public List<JsonElement> Cars { get; set; } = new();
    }

    // ─── 3. Moments ──────────────────────────────────────────────────

    public sealed class MomentSession
    {
        public string Id { get; set; } = "";
        public string CarModel { get; set; } = "";
        public string TrackName { get; set; } = "";
        public int? FinishPosition { get; set; }
        public int IncidentCount { get; set; }
        public JsonElement? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
        public string GameName { get; set; } = "";
        public string SessionType { get; set; } = "";
    }

    public sealed class MomentRating
    {
        public double IRating { get; set; }
        public double PrevIRating { get; set; }
        public string? PrevLicense { get; set; }
        public string License { get; set; } = "";
        public string? SessionType { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class MomentsResponse
    {
        public List<JsonElement> Moments { get; set; } = new();
    }

    // ─── 4. Scatter buckets ──────────────────────────────────────────

    public sealed class ScatterSession
    {
        public DateTime Date { get; set; }
        public double IRatingDelta { get; set; }
        public double SrDelta { get; set; }
        public int Incidents { get; set; }
    }

    public sealed class ScatterResponse
    {
        public List<ScatterBucket> Buckets { get; set; } = new();
    }

    public sealed class ScatterBucket
    {
        public int Day { get; set; }       // 0=Sun, 6=Sat (JS convention)
        public int Hour { get; set; }      // 0–23 in requested time zone
        public int Count { get; set; }
        public double IrDeltaSum { get; set; }
        public double SrDeltaSum { get; set; }
        public int IncidentsSum { get; set; }
        public double AvgIncidents { get; set; }
    }

    // ─── 5. When-engine ──────────────────────────────────────────────

    public sealed class WhenSession
    {
        public string Id { get; set; } = "";
        public string? UserId { get; set; }
        public string CarModel { get; set; } = "";
        public string? Manufacturer { get; set; }
        public string Category { get; set; } = "";
        public string GameName { get; set; } = "";
        public string? TrackName { get; set; }
        public string? SessionType { get; set; }
        public int? FinishPosition { get; set; }
        public int? IncidentCount { get; set; }
        public JsonElement? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class WhenRating
    {
        public string? Id { get; set; }
        public string? UserId { get; set; }
        public double IRating { get; set; }
        public double? PrevIRating { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class WhenResponse
    {
        /// <summary>WhenProfile — kept opaque, decode further at the call site.</summary>
        public JsonElement? Profile { get; set; }
        public List<WhenInsight> Insights { get; set; } = new();
        public WhenPanelView Panel { get; set; } = new();
    }

    public sealed class WhenInsight
    {
        public string Type { get; set; } = ""; // "positive" | "negative" | "neutral"
        public string Text { get; set; } = "";
    }

    /// <summary>Pre-rendered Strengths / Watch Out panel. Render verbatim.</summary>
    public sealed class WhenPanelView
    {
        public WhenPanelSide? Strengths { get; set; }
        public WhenPanelSide? WatchOut { get; set; }
    }

    public sealed class WhenPanelSide
    {
        public string Days { get; set; } = "";
        public string Hours { get; set; } = "";
        public double? AvgIRatingDelta { get; set; }
        public string Paragraph { get; set; } = "";
        public List<WhenInsight> Bullets { get; set; } = new();
    }

    // ─── 6. Race-Now verdict ─────────────────────────────────────────

    public sealed class RaceNowSessionInput
    {
        public string? TrackName { get; set; }
        public string CarModel { get; set; } = "";
        public string Category { get; set; } = "";
        public int? IncidentCount { get; set; }
        public int? CompletedLaps { get; set; }
    }

    public sealed class RaceNowResponse
    {
        public RaceNowEvaluation? Evaluation { get; set; }
        public List<RaceNowAlternative> Alternatives { get; set; } = new();
    }

    public sealed class RaceNowEvaluation
    {
        public string Verdict { get; set; } = ""; // "good" | "marginal" | "risky" | "insufficient"
        public string Detail { get; set; } = "";
        public JsonElement Stats { get; set; }
    }

    public sealed class RaceNowAlternative
    {
        public string Label { get; set; } = "";
        public string TrackName { get; set; } = "";
        public string CarModel { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    // ─── 7. Race summary ─────────────────────────────────────────────

    public sealed class RaceSummaryRequest
    {
        public WireSession Session { get; set; } = new();
        public List<JsonElement> Laps { get; set; } = new();
        public JsonElement? Behavior { get; set; }
        public RaceSummaryTrackHistory? TrackHistory { get; set; }
        public RaceSummaryRatingContext RatingContext { get; set; } = new();
    }

    public sealed class WireSession
    {
        public string Id { get; set; } = "";
        public string CarModel { get; set; } = "";
        public string? Manufacturer { get; set; }
        public string? TrackName { get; set; }
        public int? FinishPosition { get; set; }
        public int IncidentCount { get; set; }
        public string? SessionType { get; set; }
        public string Category { get; set; } = "";
        public JsonElement? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class RaceSummaryTrackHistory
    {
        public List<WireSession> Sessions { get; set; } = new();
        public int? BestPosition { get; set; }
        public double? AvgPosition { get; set; }
        public double AvgIncidents { get; set; }
        public int TotalRaces { get; set; }
    }

    public sealed class RaceSummaryRatingContext
    {
        public double? PreRaceIRating { get; set; }
        public double? PostRaceIRating { get; set; }
        public double? PreRaceSR { get; set; }
        public double? PostRaceSR { get; set; }
        public double? IrDelta { get; set; }
        public double? SrDelta { get; set; }
    }

    public sealed class RaceSummaryResponse
    {
        public RaceSummary Summary { get; set; } = new();
    }

    public sealed class RaceSummary
    {
        public string Headline { get; set; } = "";
        public string Subheadline { get; set; } = "";
        public string OverallVerdict { get; set; } = ""; // "excellent" | "good" | "mixed" | "tough" | "learning"
        public List<JsonElement> Strengths { get; set; } = new();
        public List<JsonElement> Improvements { get; set; } = new();
        public JsonElement LapAnalysis { get; set; }
        public JsonElement? ComposureReport { get; set; }
        public JsonElement? TrackContext { get; set; }
        public JsonElement? RatingImpact { get; set; }
    }

    // ─── 8. Next race ideas ──────────────────────────────────────────

    public sealed class NextRaceIdeasRequest
    {
        public List<JsonElement> Sessions { get; set; } = new();
        public List<JsonElement> Ratings { get; set; } = new();
        public List<DriverRating> DriverRatings { get; set; } = new();
        public List<JsonElement> Schedule { get; set; } = new();
        public List<string>? ActiveCategories { get; set; }
        public string? TimeZone { get; set; }
    }

    public sealed class DriverRating
    {
        public string Category { get; set; } = "";       // "road" | "oval" | "dirt_oval" | "dirt_road" | "formula"
        public double IRating { get; set; }
        public string SafetyRating { get; set; } = "";   // e.g. "3.45"
        public string License { get; set; } = "";        // "A" | "B" | "C" | "D" | "R"
    }

    public sealed class NextRaceIdeasResponse
    {
        public List<NextRaceIdea> Suggestions { get; set; } = new();
    }

    public sealed class NextRaceIdea
    {
        public string SeriesName { get; set; } = "";
        public string TrackName { get; set; } = "";
        public string? TrackConfig { get; set; }
        public string Category { get; set; } = "";
        public string LicenseClass { get; set; } = "";
        public bool IsOfficial { get; set; }
        public bool IsFixed { get; set; }
        public double Score { get; set; }
        public NextRaceStrategy Strategy { get; set; } = new();
        public string Commentary { get; set; } = "";
        public DateTime NextStartTime { get; set; }
        public List<string> CarClassNames { get; set; } = new();
        public int SeasonId { get; set; }
        public int SeriesId { get; set; }
        public int? RaceLapLimit { get; set; }
        public int? RaceTimeLimit { get; set; }
        public JsonElement ScoreBreakdown { get; set; }
    }

    public sealed class NextRaceStrategy
    {
        public string Type { get; set; } = ""; // "pitlane" | "conservative" | "careful" | "form" | "steady"

        /// <summary>Strategy carries free-form fields per type — keep extras opaque.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
