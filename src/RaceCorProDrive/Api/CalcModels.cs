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

    // ScatterBucket is defined in DashboardModels.cs — same shape, with
    // explicit [JsonPropertyName] attrs so the dashboard surface (which
    // doesn't set a CamelCase naming policy) deserializes correctly.

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

    // WhenInsight, WhenPanelView, WhenPanelSide are defined in
    // DashboardModels.cs — same shapes, with explicit [JsonPropertyName]
    // attrs so the dashboard surface (which doesn't set a CamelCase
    // naming policy) deserializes correctly. CalcClient's CamelCase
    // serializer respects those attrs too, so the same types work on
    // both surfaces.

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
        /// <summary>
        /// Six-tier vocabulary (mirrors server's <c>SlotTier</c>):
        /// <c>"best" | "good" | "clean-side" | "messy-side" | "risky" | "insufficient"</c>.
        /// Was four tiers earlier (good/marginal/risky/insufficient); the
        /// server moved to quantile-ranked tiers per
        /// <c>apps/web-api/src/calc/race-now-verdict.ts</c>.
        /// </summary>
        public string Verdict { get; set; } = "";
        /// <summary>
        /// Short verdict text for the chip itself, e.g. "Risky window".
        /// Required field. Missing this caused decode failures on macOS;
        /// same trap on Windows without it.
        /// </summary>
        public string Headline { get; set; } = "";
        /// <summary>
        /// One-sentence subtitle, e.g. "7.8 inc/race near 4 PM (+1.5 vs your avg)
        /// • P16.5 avg finish".
        /// </summary>
        public string Detail { get; set; } = "";
        /// <summary>
        /// Raw numbers for a "details" panel; large + evolving so kept opaque.
        /// </summary>
        public JsonElement Stats { get; set; }
    }

    /// <summary>
    /// Server type: <c>PracticeAlternative</c> from
    /// <c>apps/web-api/src/calc/race-now-verdict.ts</c>. Wire field names —
    /// <c>kind</c>, <c>title</c>, <c>detail</c>, <c>trackName?</c>,
    /// <c>carModel?</c> — not the <c>label</c>/<c>reason</c> placeholders the
    /// earlier scaffold used.
    /// </summary>
    public sealed class RaceNowAlternative
    {
        public string Kind { get; set; } = ""; // "practice" | "time-trial" | "hot-lap"
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string? TrackName { get; set; }
        public string? CarModel { get; set; }
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
