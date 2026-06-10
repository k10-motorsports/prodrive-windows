using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// The canonical module inventory for layout v2 — module ids mapped
    /// to overlay DOM selectors, show* keys, fallback sizes, and the
    /// default group layout. The overlay repo owns the source file
    /// (<c>layout-registry.json</c>, shipped via electron-builder
    /// extraResources); the host reads that bundled copy at runtime so
    /// production installs can't drift, and falls back to an embedded
    /// copy for dev runs without a built overlay. A contract test fails
    /// if the two copies diverge. Schema: docs/overlay-layout-v2.md.
    /// </summary>
    public sealed class ModuleRegistry
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public int? Version { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("modules")]
        public List<RegistryModule>? Modules { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("defaultLayout")]
        public OverlayLayout? DefaultLayout { get; set; }

        public const string EmbeddedResourceName = "RaceCorProDrive.Resources.layout-registry.json";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static ModuleRegistry Parse(string json)
        {
            var parsed = JsonSerializer.Deserialize<ModuleRegistry>(json, JsonOpts);
            if (parsed?.Modules is not { Count: > 0 })
                throw new InvalidDataException("layout-registry.json parsed empty — registry is unusable");
            return parsed;
        }

        /// <summary>
        /// Load the registry, preferring the copy bundled with the
        /// overlay (<c>{overlayDir}\resources\layout-registry.json</c>,
        /// where <paramref name="overlayBinaryPath"/> is the HUD exe the
        /// <see cref="OverlayLauncher"/> resolved). Falls back to the
        /// embedded copy when the overlay isn't installed or its copy is
        /// unreadable. Never returns null — the embedded copy is a build
        /// artifact of this assembly, so failure to load it is a broken
        /// build, not a runtime condition to limp through.
        /// </summary>
        public static ModuleRegistry LoadBundled(string? overlayBinaryPath)
        {
            if (!string.IsNullOrEmpty(overlayBinaryPath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(overlayBinaryPath);
                    if (dir != null)
                    {
                        var bundled = Path.Combine(dir, "resources", "layout-registry.json");
                        if (File.Exists(bundled))
                            return Parse(File.ReadAllText(bundled));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ModuleRegistry] bundled read failed, using embedded copy: {ex.Message}");
                }
            }
            return LoadEmbedded();
        }

        public static ModuleRegistry LoadEmbedded()
        {
            // GetExecutingAssembly rather than typeof(...).Assembly by
            // name: the test project compiles this file directly, and
            // embeds its own copy of the resource under the same
            // LogicalName, so the fallback path is exercisable there.
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidOperationException(
                    $"embedded resource '{EmbeddedResourceName}' missing — csproj EmbeddedResource entry lost?");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        public RegistryModule? Find(string moduleId)
            => Modules?.FirstOrDefault(m => m.Id == moduleId);

        /// <summary>
        /// The group a module belongs to in the default layout — the
        /// fallback when a saved layout omits the module entirely.
        /// </summary>
        public string? DefaultGroupIdFor(string moduleId)
            => DefaultLayout?.Groups?
                .FirstOrDefault(g => g.Members != null
                    && g.Members.Any(slot => slot.ModuleIds.Contains(moduleId)))?.Id;
    }

    public sealed class RegistryModule
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]        public string? Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("label")]     public string? Label { get; set; }
        /// <summary>Overlay DOM selector for the module's root element.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("selector")]  public string? Selector { get; set; }
        /// <summary>Visibility key in overlay-settings.json; null for system overlays.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("showKey")]   public string? ShowKey { get; set; }
        /// <summary>Fallback size (pre-zoom CSS px) until real metrics arrive.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("nominal")]   public MetricsSize? Nominal { get; set; }
        /// <summary>False for auto-show system overlays (race control, pit limiter, race end) — excluded from the editor.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("placeable")] public bool? Placeable { get; set; }
    }
}
