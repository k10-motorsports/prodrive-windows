using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceCorProDrive.Services
{
    // Layout v2 contract — the data the host's Visual-tab editor writes
    // into overlay-settings.json (`layout` key) and the Electron HUD's
    // v2 engine renders from. Schema + semantics: docs/overlay-layout-v2.md.
    // The JS side reads the same JSON verbatim, so key spelling here is
    // load-bearing; the shared fixture round-trip test pins it.

    /// <summary>
    /// The `layout` block: versioned list of placement groups. Nullable
    /// everywhere for the same reason as <see cref="OverlaySettings"/> —
    /// partial JSON must deserialize, and round-trips must not invent keys.
    /// </summary>
    public sealed class OverlayLayout
    {
        [JsonPropertyName("version")] public int? Version { get; set; }
        [JsonPropertyName("groups")]  public List<LayoutGroup>? Groups { get; set; }
    }

    /// <summary>
    /// One placement group: co-located modules that flow and move
    /// together (membership-only — no inter-group docking). The anchor
    /// is dual-purpose: the 9-point screen reference AND the group's
    /// own pivot. <c>locked</c> pins the group to the anchor's natural
    /// position (edge-inset); unlocked groups place their pivot at the
    /// normalized <c>x,y</c>.
    /// </summary>
    public sealed class LayoutGroup
    {
        [JsonPropertyName("id")]      public string? Id { get; set; }
        [JsonPropertyName("label")]   public string? Label { get; set; }
        [JsonPropertyName("anchor")]  public string? Anchor { get; set; }
        /// <summary>Normalized 0..1 position of the anchor point on the work area.</summary>
        [JsonPropertyName("x")]       public double? X { get; set; }
        [JsonPropertyName("y")]       public double? Y { get; set; }
        [JsonPropertyName("locked")]  public bool? Locked { get; set; }
        /// <summary>"row" | "column" — member flow inside the group.</summary>
        [JsonPropertyName("flow")]    public string? Flow { get; set; }
        /// <summary>Gap between members, pre-zoom CSS px.</summary>
        [JsonPropertyName("gap")]     public double? Gap { get; set; }
        [JsonPropertyName("members")] public List<MemberSlot>? Members { get; set; }
    }

    /// <summary>
    /// One slot in a group's flow: either a single module id (JSON
    /// string) or a stack of modules flowing perpendicular to the group
    /// (JSON string array, one nesting level). Stacks are how the
    /// default main HUD keeps controls-over-pedals, position-over-gaps
    /// and the two logo squares as vertical pairs inside a row group.
    /// </summary>
    [JsonConverter(typeof(MemberSlotConverter))]
    public sealed class MemberSlot
    {
        public MemberSlot() { }
        public MemberSlot(params string[] moduleIds) => ModuleIds = new List<string>(moduleIds);

        public List<string> ModuleIds { get; set; } = new();

        public bool IsStack => ModuleIds.Count > 1;
    }

    /// <summary>
    /// JSON shape is <c>"moduleId"</c> for single-module slots and
    /// <c>["a","b"]</c> for stacks — matching what the JS engine
    /// consumes with a plain <c>Array.isArray</c> branch.
    /// </summary>
    public sealed class MemberSlotConverter : JsonConverter<MemberSlot>
    {
        public override MemberSlot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new MemberSlot(reader.GetString()!);

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var slot = new MemberSlot();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String)
                        throw new JsonException("member stack entries must be module-id strings");
                    slot.ModuleIds.Add(reader.GetString()!);
                }
                return slot;
            }

            throw new JsonException("member slot must be a module-id string or an array of them");
        }

        public override void Write(Utf8JsonWriter writer, MemberSlot value, JsonSerializerOptions options)
        {
            if (value.ModuleIds.Count == 1)
            {
                writer.WriteStringValue(value.ModuleIds[0]);
                return;
            }
            writer.WriteStartArray();
            foreach (var id in value.ModuleIds) writer.WriteStringValue(id);
            writer.WriteEndArray();
        }
    }

    /// <summary>The 9-point anchor vocabulary shared with the JS engine.</summary>
    public static class LayoutAnchor
    {
        public const string TopLeft      = "top-left";
        public const string TopCenter    = "top-center";
        public const string TopRight     = "top-right";
        public const string MiddleLeft   = "middle-left";
        public const string Center       = "center";
        public const string MiddleRight  = "middle-right";
        public const string BottomLeft   = "bottom-left";
        public const string BottomCenter = "bottom-center";
        public const string BottomRight  = "bottom-right";

        public static readonly string[] All =
        {
            TopLeft, TopCenter, TopRight,
            MiddleLeft, Center, MiddleRight,
            BottomLeft, BottomCenter, BottomRight,
        };

        public static bool IsValid(string? anchor)
            => anchor != null && Array.IndexOf(All, anchor) >= 0;

        /// <summary>
        /// Unit pivot for an anchor — (0,0) top-left … (1,1) bottom-right.
        /// Both the editor's proxy math and the migration synthesis use
        /// this; the JS engine applies the same table as translate(-px·100%, -py·100%).
        /// </summary>
        public static (double Px, double Py) Pivot(string anchor) => anchor switch
        {
            TopLeft      => (0.0, 0.0),
            TopCenter    => (0.5, 0.0),
            TopRight     => (1.0, 0.0),
            MiddleLeft   => (0.0, 0.5),
            Center       => (0.5, 0.5),
            MiddleRight  => (1.0, 0.5),
            BottomLeft   => (0.0, 1.0),
            BottomCenter => (0.5, 1.0),
            BottomRight  => (1.0, 1.0),
            _ => throw new ArgumentException($"unknown anchor '{anchor}'", nameof(anchor)),
        };
    }

    // ── Metrics sidecar (overlay-metrics.json) ─────────────────────
    // Observed state, written by the overlay only (one writer per file;
    // the settings file's writer is the host). All values are VISUAL px
    // (post-zoom getBoundingClientRect) so the editor can scale proxies
    // pixel-exactly without re-deriving zoom math.

    public sealed class OverlayMetrics
    {
        [JsonPropertyName("version")]  public int? Version { get; set; }
        [JsonPropertyName("viewport")] public MetricsSize? Viewport { get; set; }
        /// <summary>Zoom scale in effect when measured (1.65 == 165%).</summary>
        [JsonPropertyName("zoom")]     public double? Zoom { get; set; }
        /// <summary>Module id → measured size.</summary>
        [JsonPropertyName("modules")]  public Dictionary<string, MetricsSize>? Modules { get; set; }
        /// <summary>Group id → measured screen rect.</summary>
        [JsonPropertyName("groups")]   public Dictionary<string, MetricsRect>? Groups { get; set; }
    }

    public sealed class MetricsSize
    {
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
    }

    public sealed class MetricsRect
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
    }
}
