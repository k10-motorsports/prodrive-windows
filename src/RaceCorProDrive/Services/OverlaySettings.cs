using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Strongly-typed mirror of the JSON the Electron HUD reads/writes
    /// at <c>%APPDATA%\RaceCor.io\overlay-settings.json</c>. Property
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
        [JsonPropertyName("greenScreen")]     public bool? GreenScreen { get; set; }
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
        [JsonPropertyName("coachTone")]                   public string? CoachTone { get; set; }
        [JsonPropertyName("coachDepth")]                  public string? CoachDepth { get; set; }

        // ── Recording / replay buffer ──
        [JsonPropertyName("recordingQuality")]   public string? RecordingQuality { get; set; }
        [JsonPropertyName("recordingMicEnabled")] public bool? RecordingMicEnabled { get; set; }
        [JsonPropertyName("recordingMicDevice")] public string? RecordingMicDevice { get; set; }
        [JsonPropertyName("replayBufferEnabled")] public bool? ReplayBufferEnabled { get; set; }
        [JsonPropertyName("replayBufferSeconds")] public int? ReplayBufferSeconds { get; set; }

        // ── Advanced ──
        [JsonPropertyName("incPenalty")]        public bool? IncPenalty { get; set; }
        [JsonPropertyName("incDQ")]             public bool? IncDQ { get; set; }
        [JsonPropertyName("rallyMode")]         public bool? RallyMode { get; set; }
        [JsonPropertyName("driveMode")]         public string? DriveMode { get; set; }
        [JsonPropertyName("forceFlag")]         public string? ForceFlag { get; set; }
        [JsonPropertyName("iracingDataSync")]   public bool? IracingDataSync { get; set; }
        [JsonPropertyName("apiBase")]           public string? ApiBase { get; set; }
        [JsonPropertyName("agentKey")]          public string? AgentKey { get; set; }
        [JsonPropertyName("useRemoteTokens")]   public bool? UseRemoteTokens { get; set; }

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
        public static OverlaySettings Apply(OverlaySettings input)
        {
            // Display
            input.LogoOnlyStart       ??= true;
            input.LayoutPosition      ??= "top-right";
            input.Zoom                ??= 1.0;
            input.ShowBorders         ??= false;
            input.ShowWebGL           ??= true;
            input.AmbientMode         ??= "auto";
            input.GreenScreen         ??= false;
            input.VisualPreset        ??= "standard";
            input.Theme               ??= "default";

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

            // Commentary
            input.CommentaryPromptDuration  ??= 8;
            input.CommentaryShowTopicTitle  ??= true;
            input.CommentaryEventOnlyMode   ??= false;
            input.CommentaryDemoMode        ??= false;
            input.CommentaryCatStrategy     ??= true;
            input.CommentaryCatTrack        ??= true;
            input.CommentaryCatRivals       ??= true;
            input.CommentaryCatBehavior     ??= true;
            input.CoachTone                 ??= "neutral";
            input.CoachDepth                ??= "balanced";

            // Recording
            input.RecordingQuality      ??= "high";
            input.RecordingMicEnabled   ??= false;
            input.ReplayBufferEnabled   ??= false;
            input.ReplayBufferSeconds   ??= 30;

            // Advanced
            input.IncPenalty       ??= true;
            input.IncDQ            ??= true;
            input.RallyMode        ??= false;
            input.DriveMode        ??= "circuit";
            input.ForceFlag        ??= "auto";
            input.IracingDataSync  ??= true;
            input.ApiBase          ??= "https://prodrive.racecor.io";
            input.UseRemoteTokens  ??= false;

            // Host
            input.WinUIAutoLaunchHud  ??= false;
            input.WinUILaunchOnLogin  ??= false;

            return input;
        }
    }
}
