using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RaceCorProDrive.Services;
using Xunit;

namespace RaceCorProDrive.Tests.Services;

/// <summary>
/// Layout v2 contract tests — the C# mirror of the overlay repo's
/// tests/layout-contract.test.mjs. Both suites validate the SAME
/// canonical artifacts (layout-registry.json + the layout-v2-settings
/// fixture, byte-identical copies in each repo), so a schema change
/// that lands on only one side fails fast. Plan: docs/overlay-layout-v2.md.
/// </summary>
public class LayoutContractTests
{
    // Mirrors OverlaySettingsService.JsonOpts — the options the real
    // save path uses. The round-trip test is only meaningful with the
    // production serializer settings.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "layout-v2-settings.json");

    // ── Fixture round-trip ─────────────────────────────────────────

    [Fact]
    public void Fixture_SurvivesHostRoundTrip_KeyForKey()
    {
        // The host loads the file, mutates one control, and serializes
        // the WHOLE object back. Nothing the fixture carries — legacy
        // keys, the v2 layout block, member-slot stacks — may be lost
        // or reshaped by that trip.
        var json = File.ReadAllText(FixturePath);
        var parsed = JsonSerializer.Deserialize<OverlaySettings>(json, JsonOpts)!;
        var rewritten = JsonSerializer.Serialize(parsed, JsonOpts);

        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(rewritten))
            .Should().BeTrue("the host's load→save round-trip must preserve the fixture exactly\n" +
                             $"original:\n{json}\nrewritten:\n{rewritten}");
    }

    [Fact]
    public void Fixture_LayoutBlock_ParsesIntoTypedModel()
    {
        var parsed = JsonSerializer.Deserialize<OverlaySettings>(
            File.ReadAllText(FixturePath), JsonOpts)!;

        parsed.Layout.Should().NotBeNull();
        parsed.Layout!.Version.Should().Be(2);
        parsed.Layout.Groups.Should().HaveCount(7);

        var mainHud = parsed.Layout.Groups!.Single(g => g.Id == "mainHud");
        mainHud.Anchor.Should().Be(LayoutAnchor.TopRight);
        mainHud.Locked.Should().BeTrue();
        mainHud.Flow.Should().Be("row");
        // Slot structure: [controls+pedals] maps [position+gaps] tacho [k10+car]
        mainHud.Members.Should().HaveCount(5);
        mainHud.Members![0].IsStack.Should().BeTrue();
        mainHud.Members[0].ModuleIds.Should().Equal("controls", "pedals");
        mainHud.Members[1].IsStack.Should().BeFalse();
        mainHud.Members[1].ModuleIds.Should().Equal("maps");

        var commentary = parsed.Layout.Groups!.Single(g => g.Id == "commentary");
        commentary.Locked.Should().BeFalse();
        commentary.X.Should().Be(0.012);
        commentary.Flow.Should().Be("column");
    }

    // ── MemberSlot converter ───────────────────────────────────────

    [Theory]
    [InlineData("\"tacho\"", new[] { "tacho" })]
    [InlineData("[\"controls\",\"pedals\"]", new[] { "controls", "pedals" })]
    public void MemberSlot_ReadsStringAndArrayForms(string json, string[] expected)
    {
        var slot = JsonSerializer.Deserialize<MemberSlot>(json, JsonOpts)!;
        slot.ModuleIds.Should().Equal(expected);
        slot.IsStack.Should().Be(expected.Length > 1);
    }

    [Fact]
    public void MemberSlot_WritesSingleAsString_StackAsArray()
    {
        // Compact options — JsonOpts indents, which is irrelevant here.
        var compact = new JsonSerializerOptions();
        JsonSerializer.Serialize(new MemberSlot("tacho"), compact).Should().Be("\"tacho\"");
        JsonSerializer.Serialize(new MemberSlot("a", "b"), compact).Should().Be("[\"a\",\"b\"]");
    }

    // ── Registry (embedded fallback copy) ──────────────────────────

    [Fact]
    public void Registry_Embedded_IsWellFormed()
    {
        var reg = ModuleRegistry.LoadEmbedded();

        reg.Version.Should().Be(2);
        reg.Modules.Should().NotBeNull();
        reg.Modules!.Count.Should().BeGreaterOrEqualTo(16, "full module inventory expected");
        reg.Modules.Select(m => m.Id).Should().OnlyHaveUniqueItems();

        foreach (var m in reg.Modules)
        {
            m.Id.Should().NotBeNullOrEmpty();
            m.Label.Should().NotBeNullOrEmpty();
            m.Selector.Should().NotBeNullOrEmpty();
            if (m.Placeable == true)
            {
                m.ShowKey.Should().NotBeNullOrEmpty($"placeable module {m.Id} needs a showKey");
                m.Nominal.Should().NotBeNull($"placeable module {m.Id} needs a nominal fallback size");
                m.Nominal!.W.Should().BeGreaterThan(0);
                m.Nominal.H.Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public void Registry_DefaultLayout_CoversEveryPlaceableModuleExactlyOnce()
    {
        var reg = ModuleRegistry.LoadEmbedded();
        var placeable = reg.Modules!.Where(m => m.Placeable == true).Select(m => m.Id!).ToHashSet();

        var seen = new Dictionary<string, string>(); // moduleId → groupId
        foreach (var g in reg.DefaultLayout!.Groups!)
        {
            LayoutAnchor.IsValid(g.Anchor).Should().BeTrue($"group {g.Id} has anchor '{g.Anchor}'");
            g.X.Should().BeInRange(0, 1);
            g.Y.Should().BeInRange(0, 1);
            g.Flow.Should().BeOneOf("row", "column");
            g.Locked.Should().NotBeNull();

            foreach (var id in g.Members!.SelectMany(s => s.ModuleIds))
            {
                placeable.Should().Contain(id, $"group {g.Id} references {id}");
                seen.Should().NotContainKey(id, $"{id} must not appear in two groups");
                seen[id] = g.Id!;
            }
        }

        seen.Keys.Should().BeEquivalentTo(placeable,
            "every placeable module must be in exactly one default group");
    }

    [Fact]
    public void Registry_DefaultGroupLookup_ResolvesThroughSlots()
    {
        var reg = ModuleRegistry.LoadEmbedded();
        reg.DefaultGroupIdFor("pedals").Should().Be("mainHud", "pedals lives inside a stack slot");
        reg.DefaultGroupIdFor("leaderboard").Should().Be("secondary");
        reg.DefaultGroupIdFor("raceControl").Should().BeNull("system overlays are not in the default layout");
    }

    [Fact]
    public void Registry_DefaultLayout_MatchesFixtureLayoutBlock()
    {
        // One canonical seed: the fixture's layout block IS the
        // registry's defaultLayout. The JS suite asserts the same.
        var reg = ModuleRegistry.LoadEmbedded();
        var fixture = JsonSerializer.Deserialize<OverlaySettings>(
            File.ReadAllText(FixturePath), JsonOpts)!;

        var a = JsonSerializer.Serialize(reg.DefaultLayout, JsonOpts);
        var b = JsonSerializer.Serialize(fixture.Layout, JsonOpts);
        JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b)).Should().BeTrue(
            $"registry defaultLayout and fixture layout diverged:\n{a}\nvs\n{b}");
    }

    // ── Cross-repo drift ───────────────────────────────────────────

    [Fact]
    public void Registry_And_Fixture_MatchOverlayRepoCopies_WhenPresent()
    {
        // The overlay repo owns the canonical files; this repo carries
        // byte-identical copies. Only verifiable on a machine with both
        // checkouts side by side (dev boxes) — CI for this repo can't
        // see the sibling, so absence skips rather than fails.
        var overlayRoot = FindSiblingOverlayRepo();
        if (overlayRoot == null) return; // skip: sibling checkout not present

        var pairs = new[]
        {
            (Ours: Path.Combine(AppContext.BaseDirectory, "Fixtures", "layout-v2-settings.json"),
             Theirs: Path.Combine(overlayRoot, "tests", "fixtures", "layout-v2-settings.json")),
        };

        foreach (var (ours, theirs) in pairs)
        {
            File.Exists(theirs).Should().BeTrue($"expected canonical copy at {theirs}");
            Normalize(File.ReadAllText(ours)).Should().Be(Normalize(File.ReadAllText(theirs)),
                $"{Path.GetFileName(ours)} drifted between repos — re-copy from the overlay repo");
        }

        // Registry: our embedded copy vs the overlay's source of truth.
        var theirRegistry = Path.Combine(overlayRoot, "layout-registry.json");
        File.Exists(theirRegistry).Should().BeTrue();
        var oursEmbedded = JsonSerializer.Serialize(ModuleRegistry.LoadEmbedded(), JsonOpts);
        var theirsParsed = JsonSerializer.Serialize(
            ModuleRegistry.Parse(File.ReadAllText(theirRegistry)), JsonOpts);
        oursEmbedded.Should().Be(theirsParsed,
            "embedded layout-registry.json drifted from the overlay repo's canonical copy");

        static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();
    }

    private static string? FindSiblingOverlayRepo()
    {
        // Worktrees nest under prodrive-windows/.claude/worktrees/, so
        // walk far enough up to clear both the bin dir and the worktree.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "prodrive-overlay");
            if (File.Exists(Path.Combine(candidate, "layout-registry.json")))
                return candidate;
        }
        return null;
    }

    // ── Inventory reconciliation defaults ──────────────────────────

    [Fact]
    public void Apply_DefaultsNewVisibilityKeysOn_AndLeavesDeadKeysAlone()
    {
        var input = new OverlaySettings();
        OverlaySettingsDefaults.Apply(input);

        input.ShowMaps.Should().BeTrue("maps gained a toggle in the v2 reconciliation");
        input.ShowTimer.Should().BeTrue("race timer gained a toggle in the v2 reconciliation");
#pragma warning disable CS0618 // intentionally probing the dead keys
        input.ShowFuel.Should().BeNull("dead key must no longer be backfilled");
        input.ShowTyres.Should().BeNull("dead key must no longer be backfilled");
#pragma warning restore CS0618
    }

    [Fact]
    public void UnknownKeys_SurviveHostRoundTrip_ViaExtensionData()
    {
        // Keys only the overlay knows (performanceMode, dsShow*, …)
        // must not be stripped when the host rewrites the file.
        const string json = """
            { "zoom": 165, "performanceMode": true, "dsShowGforce": false }
            """;
        var parsed = JsonSerializer.Deserialize<OverlaySettings>(json, JsonOpts)!;
        var rewritten = JsonSerializer.Serialize(parsed, JsonOpts);

        var node = JsonNode.Parse(rewritten)!;
        node["performanceMode"]!.GetValue<bool>().Should().BeTrue();
        node["dsShowGforce"]!.GetValue<bool>().Should().BeFalse();
    }
}
