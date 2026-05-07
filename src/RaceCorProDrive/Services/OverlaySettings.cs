using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Strongly-typed mirror of the JSON the Electron HUD reads/writes
    /// at <c>%APPDATA%\K10 Motorsports\overlay-settings.json</c>. Property
    /// names match the lowerCamelCase keys the HUD's <c>config.js</c>
    /// already emits — the WinUI host is now the canonical writer of
    /// this file, but the HUD still owns the read side, so the schema
    /// here can't drift without coordinated changes there.
    /// </summary>
    /// <remarks>
    /// Not every key is present in every saved file (older saves
    /// predate later additions). All properties are nullable so
    /// deserialization succeeds against partial JSON, and the SettingsPage
    /// applies its own defaults via <see cref="GetWithDefaults"/> rather
    /// than baking them into property initializers (otherwise round-
    /// tripping would clobber user-removed keys with defaults).
    /// </remarks>
    public sealed class OverlaySettings
    {
        // ── Display ──
        [JsonPropertyName("logoOnlyStart")]   public bool? LogoOnlyStart { get; set; }
        [JsonPropertyName("layoutPosition")]  public string? LayoutPosition { get; set; }
        [JsonPropertyName("zoom")]            public double? Zoom { get; set; }
        [JsonPropertyName("showBorders")]     public bool? ShowBorders { get; set; }
        [JsonPropertyName("showWebGL")]       public bool? ShowWebGL { get; set; }
        [JsonPropertyName("ambientMode")]     public string? AmbientMode { get; set; }
        // Screen-space rectangle (0..1 ratios of the primary display)
        // that the C# plugin's ScreenColorSampler captures for ambient
        // light. Written by the host's region picker; the overlay
        // forwards it to the plugin via setrect HTTP on next applySettings.
        [JsonPropertyName("ambientCaptureRect")] public AmbientRect? AmbientCaptureRect { get; set; }
        [JsonPropertyName("visualPreset")]    public string? VisualPreset { get; set; }
        [JsonPropertyName("theme")]           public string? Theme { get; set; }

        // ── Component visibility ──
        [JsonPropertyName("showFuel")]         public bool? ShowFuel { get; set; }
        [JsonPropertyName("showTyres")]        public bool? ShowTyres { get; set; }
        [JsonPropertyName("showControls")]     public bool? ShowControls { get; set; }
        [JsonPropertyName("showPedals")]       public bool? ShowPedals { get; set; }
        [JsonPropertyName("showPosition")]     public bool? ShowPosition { get; set; }
        [JsonPropertyName("showTacho")]        public bool? ShowTacho { get; set; }
        [JsonPropertyName("showCommentary")]   public bool? ShowCommentary { get; set; }
        [JsonPropertyName("showK10Logo")]      public bool? ShowK10Logo { get; set; }
        [JsonPropertyName("showCarLogo")]      public bool? ShowCarLogo { get; set; }
        [JsonPropertyName("showGameLogo")]     public bool? ShowGameLogo { get; set; }
        [JsonPropertyName("showLeaderboard")]  public bool? ShowLeaderboard { get; set; }
        [JsonPropertyName("showDatastream")]   public bool? ShowDatastream { get; set; }
        [JsonPropertyName("showPitBox")]       public bool? ShowPitBox { get; set; }
        [JsonPropertyName("showIncidents")]    public bool? ShowIncidents { get; set; }
        [JsonPropertyName("showSpotter")]      public bool? ShowSpotter { get; set; }

        // ── Layout / display extensions ──
        [JsonPropertyName("bottomYOffset")]   public int? BottomYOffset { get; set; }
        // Per-group anchor map (PR-B: drag/drop visual editor writes
        // here). 6 zones: top-left, top-center, top-right, bottom-left,
        // bottom-center, bottom-right. Keys are group identifiers
        // (e.g. "tachoBlock", "leaderboardPanel", "spotterPanel").
        [JsonPropertyName("groupPositions")]  public Dictionary<string, string>? GroupPositions { get; set; }

        // ── Branding (was BrandingSection on web) ──
        [JsonPropertyName("logoSubtitle")]    public string? LogoSubtitle { get; set; }

        // ── Leaderboard ──
        [JsonPropertyName("lbFocus")]         public string? LbFocus { get; set; }
        [JsonPropertyName("lbMaxRows")]       public int? LbMaxRows { get; set; }
        [JsonPropertyName("lbExpandToFill")]  public bool? LbExpandToFill { get; set; }

        // ── Commentary ──
        [JsonPropertyName("commentaryPromptDuration")]    public int? CommentaryPromptDuration { get; set; }
        [JsonPropertyName("commentaryShowTopicTitle")]    public bool? CommentaryShowTopicTitle { get; set; }
        [JsonPropertyName("commentaryEventOnlyMode")]     public bool? CommentaryEventOnlyMode { get; set; }
        [JsonPropertyName("commentaryDemoMode")]          public bool? CommentaryDemoMode { get; set; }
        [JsonPropertyName("commentaryCatStrategy")]       public bool? CommentaryCatStrategy { get; set; }
        [JsonPropertyName("commentaryCatTrack")]          public bool? CommentaryCatTrack { get; set; }
        [JsonPropertyName("commentaryCatRivals")]         public bool? CommentaryCatRivals { get; set; }
        [JsonPropertyName("commentaryCatBehavior")]       public bool? CommentaryCatBehavior { get; set; }
        [JsonPropertyName("commentaryDriverFirstName")]   public string? CommentaryDriverFirstName { get; set; }
        [JsonPropertyName("commentaryDriverLastName")]    public string? CommentaryDriverLastName { get; set; }
        // Coach context categories (the user describing their setup
        // and skill so the coach commentary tunes itself).
        [JsonPropertyName("commentaryCatHardware")]       public bool? CommentaryCatHardware { get; set; }
        [JsonPropertyName("commentaryCatGameFeel")]       public bool? CommentaryCatGameFeel { get; set; }
        [JsonPropertyName("commentaryCatCarResponse")]    public bool? CommentaryCatCarResponse { get; set; }
        [JsonPropertyName("commentaryCatRacingExperience")] public bool? CommentaryCatRacingExperience { get; set; }
        [JsonPropertyName("coachTone")]                   public string? CoachTone { get; set; }
        [JsonPropertyName("coachDepth")]                  public string? CoachDepth { get; set; }

        // ── Recording / replay buffer ──
        [JsonPropertyName("recordingQuality")]            public string? RecordingQuality { get; set; }
        // Key is "recordingMic" (not "recordingMicEnabled") to match what
        // the HUD's recorder.js already reads from config.js.
        [JsonPropertyName("recordingMic")]                public bool? RecordingMicEnabled { get; set; }
        [JsonPropertyName("recordingMicDevice")]          public string? RecordingMicDevice { get; set; }
        // Mic + system-audio volumes stored as 0..1 floats (web slider
        // scales by *100 for display).
        [JsonPropertyName("recordingMicVolume")]          public double? RecordingMicVolume { get; set; }
        [JsonPropertyName("recordingSystemAudioDevice")]  public string? RecordingSystemAudioDevice { get; set; }
        [JsonPropertyName("recordingSystemVolume")]       public double? RecordingSystemVolume { get; set; }
        [JsonPropertyName("recordingWebcamDevice")]       public string? RecordingWebcamDevice { get; set; }
        [JsonPropertyName("recordingFacecamSize")]        public string? RecordingFacecamSize { get; set; }
        [JsonPropertyName("recordingFacecamPos")]         public string? RecordingFacecamPos { get; set; }
        [JsonPropertyName("recordingOutputFormat")]       public string? RecordingOutputFormat { get; set; }
        [JsonPropertyName("recordingEncoder")]            public string? RecordingEncoder { get; set; }
        [JsonPropertyName("recordingDeleteSource")]       public bool? RecordingDeleteSource { get; set; }
        [JsonPropertyName("recordingAutoRecord")]         public bool? RecordingAutoRecord { get; set; }
        [JsonPropertyName("recordingAutoStopOnPit")]      public bool? RecordingAutoStopOnPit { get; set; }
        [JsonPropertyName("replayBufferEnabled")]         public bool? ReplayBufferEnabled { get; set; }
        [JsonPropertyName("replayBufferSeconds")]         public int? ReplayBufferSeconds { get; set; }
        // Web schema uses "replayBufferDuration" (string seconds);
        // older HUD reads "replayBufferSeconds" (int). Keep both;
        // SaveAsync mirrors the new value to both.
        [JsonPropertyName("replayBufferDuration")]        public string? ReplayBufferDuration { get; set; }

        // ── System ──
        [JsonPropertyName("iracingDataSync")]   public bool? IracingDataSync { get; set; }
        [JsonPropertyName("apiBase")]           public string? ApiBase { get; set; }
        [JsonPropertyName("agentKey")]          public string? AgentKey { get; set; }
        [JsonPropertyName("useRemoteTokens")]   public bool? UseRemoteTokens { get; set; }
        // Local SimHub plugin URL (for the host's loopback HTTP/WS
        // talk-to-plugin pipeline). Defaults to the plugin's own
        // bound port; settable for unusual setups.
        [JsonPropertyName("simhubUrl")]         public string? SimHubUrl { get; set; }

        // ── DEPRECATED (Race Rules, removed from UI) ──
        // Kept on the model so existing on-disk JSON deserializes
        // without losing keys; they're never surfaced in the new
        // SettingsPage. Overlay still reads them harmlessly. Schedule
        // a sweep of overlay code to drop the reads, then remove from
        // the model entirely.
        [Obsolete("Race-rule incident counts now come from iRacing telemetry, not user setting. Removed from UI.")]
        [JsonPropertyName("incPenalty")]        public bool? IncPenalty { get; set; }
        [Obsolete("Race-rule incident counts now come from iRacing telemetry, not user setting. Removed from UI.")]
        [JsonPropertyName("incDQ")]             public bool? IncDQ { get; set; }
        [Obsolete("Drive mode never wired up. Removed from UI.")]
        [JsonPropertyName("driveMode")]         public string? DriveMode { get; set; }
        [Obsolete("Rally mode never wired up. Removed from UI.")]
        [JsonPropertyName("rallyMode")]         public bool? RallyMode { get; set; }
        [Obsolete("Force flag was a dev-only toggle. Removed from UI.")]
        [JsonPropertyName("forceFlag")]         public string? ForceFlag { get; set; }

        // ── Ambient capture rect (0..1 ratios of primary display) ──
        // Stored as nested object so the JSON shape matches what the
        // overlay's ambient-capture.js already reads from
        // `_settings.ambientCaptureRect`.

        // ── WinUI host extensions (not consumed by the HUD) ──
        // The HUD ignores these on read, so we tuck WinUI-specific
        // toggles like "auto-start the HUD when the host launches"
        // into the same file rather than maintaining a parallel one.
        [JsonPropertyName("winuiAutoLaunchHud")]  public bool? WinUIAutoLaunchHud { get; set; }
        [JsonPropertyName("winuiLaunchOnLogin")]  public bool? WinUILaunchOnLogin { get; set; }
    }

    /// <summary>
    /// Defaults applied when a key is missing from the on-disk file.
    /// Mirrors the fallback values in the HUD's <c>config.js</c> so
    /// the WinUI Settings UI shows the same starting state the HUD
    /// would have used. Centralized here so any later default change
    /// only happens once.
    /// </summary>
    public static class OverlaySettingsDefaults
    {
#pragma warning disable CS0618 // intentional: backfill obsolete fields so old JSON round-trips
        public static OverlaySettings Apply(OverlaySettings input)
        {
            // ── Migrations from buggy 0.18.x writes ────────────────
            // The host briefly stored Zoom as a 0.5..2.0 normalized
            // scale, but the overlay has always expected a percentage
            // (50..200). Anything < 5 is by definition a stale
            // normalized value — promote it to a percentage so the
            // overlay isn't trying to render at 1% scale.
            if (input.Zoom is double z && z > 0 && z < 5) input.Zoom = z * 100.0;
            // Theme "default" was a host-only string the overlay never
            // had a stylesheet for. Coerce to the overlay's actual
            // fallback so a user who was on the broken default stops
            // seeing an unstyled HUD.
            if (string.Equals(input.Theme, "default", StringComparison.OrdinalIgnoreCase))
                input.Theme = "dark";
            // Earlier host versions saved SimHubUrl as bare host:port
            // ("http://localhost:8889"). The plugin endpoint lives at
            // /racecor-io-pro-drive/ — without it the renderer fetches
            // the SimHub root, gets a non-JSON response, and every
            // panel sits at zero. Append the path on read so old saves
            // self-heal. Anything that already contains the path or
            // points at a different host (remote SimHub) is left alone.
            if (!string.IsNullOrEmpty(input.SimHubUrl)
                && !input.SimHubUrl.Contains("/racecor-io-pro-drive", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = input.SimHubUrl.TrimEnd('/');
                input.SimHubUrl = trimmed + "/racecor-io-pro-drive/";
            }

            // Display
            input.LogoOnlyStart       ??= true;
            input.LayoutPosition      ??= "top-right";
            // Zoom is a *percentage* (100 = 100%, 165 = 165%) — the
            // overlay's settings.js does `(val || 100) / 100`, so a
            // host-side default of 1.0 collapses the dashboard to 1%
            // (and a host-set "1.5" reads as 0.015%). Mirror the
            // overlay's own default of 165 to keep the on-disk file
            // round-trip-stable.
            input.Zoom                ??= 165.0;
            input.BottomYOffset       ??= 0;
            input.ShowBorders         ??= false;
            input.ShowWebGL           ??= true;
            input.AmbientMode         ??= "auto";
            input.VisualPreset        ??= "standard";
            // Overlay's data-theme attribute drives CSS theme rules;
            // there is no "default" rule, so writing "default" produces
            // an unstyled overlay. Match the overlay's own fallback.
            input.Theme               ??= "dark";

            // Branding
            input.LogoSubtitle        ??= "";

            // Components — most default ON, matching the HUD's defaults.
            input.ShowFuel            ??= true;
            input.ShowTyres           ??= true;
            input.ShowControls        ??= true;
            input.ShowPedals          ??= true;
            input.ShowPosition        ??= true;
            input.ShowTacho           ??= true;
            input.ShowCommentary      ??= true;
            input.ShowK10Logo         ??= true;
            input.ShowCarLogo         ??= true;
            input.ShowGameLogo        ??= true;
            input.ShowLeaderboard     ??= true;
            input.ShowDatastream      ??= true;
            input.ShowPitBox          ??= true;
            input.ShowIncidents       ??= true;
            input.ShowSpotter         ??= true;

            // Leaderboard
            input.LbFocus             ??= "self";
            input.LbMaxRows           ??= 6;
            input.LbExpandToFill      ??= false;

            // Commentary
            input.CommentaryPromptDuration       ??= 8;
            input.CommentaryShowTopicTitle       ??= true;
            input.CommentaryEventOnlyMode        ??= false;
            input.CommentaryDemoMode             ??= false;
            input.CommentaryCatStrategy          ??= true;
            input.CommentaryCatTrack             ??= true;
            input.CommentaryCatRivals            ??= true;
            input.CommentaryCatBehavior          ??= true;
            input.CommentaryCatHardware          ??= true;
            input.CommentaryCatGameFeel          ??= true;
            input.CommentaryCatCarResponse       ??= true;
            input.CommentaryCatRacingExperience  ??= true;
            input.CoachTone                      ??= "neutral";
            input.CoachDepth                     ??= "balanced";

            // Recording
            input.RecordingQuality          ??= "high";
            // HUD's config.js defaults this to true; mirror so the host
            // SettingsPage shows the same starting state.
            input.RecordingMicEnabled       ??= true;
            input.RecordingMicVolume        ??= 0.8;
            input.RecordingSystemVolume     ??= 1.0;
            input.RecordingFacecamSize      ??= "small";
            input.RecordingFacecamPos       ??= "bottom-right";
            input.RecordingOutputFormat     ??= "mp4";
            input.RecordingEncoder          ??= "auto";
            input.RecordingDeleteSource     ??= true;
            input.RecordingAutoRecord       ??= false;
            input.RecordingAutoStopOnPit    ??= false;
            input.ReplayBufferEnabled       ??= false;
            input.ReplayBufferSeconds       ??= 30;
            input.ReplayBufferDuration      ??= "30";

            // System
            input.IracingDataSync  ??= true;
            input.ApiBase          ??= "https://prodrive.racecor.io";
            input.UseRemoteTokens  ??= false;
            // Must include the plugin path — the overlay's poll-engine
            // hits this URL directly and SimHub root returns the host
            // page, not the plugin's JSON. If only the host:port lands
            // here, the renderer falls back to the canonical URL it
            // already has in config.js.
            input.SimHubUrl        ??= "http://localhost:8889/racecor-io-pro-drive/";

            // Host
            input.WinUIAutoLaunchHud  ??= false;
            input.WinUILaunchOnLogin  ??= false;

            return input;
        }
#pragma warning restore CS0618
    }

    /// <summary>
    /// Rectangle stored as 0..1 ratios of the primary display. The
    /// overlay's ambient-capture.js consumes this same shape via
    /// <c>_settings.ambientCaptureRect</c> and forwards it to the C#
    /// SimHub plugin's ScreenColorSampler endpoint.
    /// </summary>
    public sealed class AmbientRect
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
    }
}
