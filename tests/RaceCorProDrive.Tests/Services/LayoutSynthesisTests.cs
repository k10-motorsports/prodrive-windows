using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RaceCorProDrive.Services;
using Xunit;

namespace RaceCorProDrive.Tests.Services;

/// <summary>
/// LayoutSynthesis — legacy corner keys → v2 layout. The reference
/// behavior is the overlay's old applyLayout derivation table plus the
/// dashboard.css flex rules (row-reverse flips), pinned here so the
/// migration can't drift from what users actually see on screen.
/// </summary>
public class LayoutSynthesisTests
{
    private const double W = 2560, H = 1440;

    private static OverlayLayout Synthesize(string? position, int? bottomYOffset = null)
        => LayoutSynthesis.FromLegacy(
            new OverlaySettings { LayoutPosition = position, BottomYOffset = bottomYOffset },
            ModuleRegistry.LoadEmbedded(), W, H);

    private static LayoutGroup G(OverlayLayout layout, string id)
        => layout.Groups!.Single(g => g.Id == id);

    [Fact]
    public void TopRight_IsExactlyTheCanonicalDefaultLayout()
    {
        // The registry's defaultLayout IS the top-right derivation —
        // one seed, two routes to it. Deep equality keeps them fused.
        var synthesized = Synthesize("top-right");
        var canonical = ModuleRegistry.LoadEmbedded().DefaultLayout!;

        var a = JsonSerializer.Serialize(synthesized);
        var b = JsonSerializer.Serialize(canonical);
        JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b)).Should().BeTrue(
            $"synthesis(top-right) must equal registry defaultLayout:\n{a}\nvs\n{b}");
    }

    [Fact]
    public void NullPosition_DefaultsToTopRight()
    {
        JsonSerializer.Serialize(Synthesize(null))
            .Should().Be(JsonSerializer.Serialize(Synthesize("top-right")));
    }

    [Fact]
    public void BottomLeft_DerivesEveryDependentPlacement()
    {
        var layout = Synthesize("bottom-left");

        var main = G(layout, "mainHud");
        main.Anchor.Should().Be(LayoutAnchor.BottomLeft);
        main.Locked.Should().BeTrue();
        // Left corners rendered row-reverse → logos hug the left edge.
        main.Members![0].ModuleIds.Should().Equal("k10Logo", "carLogo");
        main.Members[^1].ModuleIds.Should().Equal("controls", "pedals");

        // Secondary: opposite vertical (top), same horizontal (left),
        // leaderboard at the screen edge → first in LTR order.
        var sec = G(layout, "secondary");
        sec.Anchor.Should().Be(LayoutAnchor.TopLeft);
        sec.Members!.SelectMany(s => s.ModuleIds)
            .Should().Equal("leaderboard", "datastream", "pitbox");

        // Incidents: secondary's vertical, opposite horizontal.
        G(layout, "incidents").Anchor.Should().Be(LayoutAnchor.TopRight);

        // Commentary: diagonal from the main HUD (top-right), free.
        var cmt = G(layout, "commentary");
        cmt.Anchor.Should().Be(LayoutAnchor.TopRight);
        cmt.Locked.Should().BeFalse();
        cmt.X.Should().Be(0.988);
        cmt.Y.Should().Be(0.3);

        // Timer rides the main corner, free, mirrored x.
        var timer = G(layout, "timer");
        timer.Anchor.Should().Be(LayoutAnchor.BottomLeft);
        timer.X.Should().Be(0.065);
        timer.Y.Should().Be(0.825);
    }

    [Fact]
    public void TopLeft_SecondaryGoesBottomLeft_LeaderboardAtEdge()
    {
        var layout = Synthesize("top-left");
        var sec = G(layout, "secondary");
        sec.Anchor.Should().Be(LayoutAnchor.BottomLeft);
        sec.Members![0].ModuleIds.Should().Equal("leaderboard");
        G(layout, "incidents").Anchor.Should().Be(LayoutAnchor.BottomRight);
        G(layout, "commentary").Anchor.Should().Be(LayoutAnchor.BottomRight);
    }

    [Fact]
    public void BottomRight_SecondaryGoesTopRight_PitboxFirstInFlow()
    {
        var layout = Synthesize("bottom-right");
        var sec = G(layout, "secondary");
        sec.Anchor.Should().Be(LayoutAnchor.TopRight);
        // row-reverse legacy → leaderboard hugs the right edge → LAST in LTR.
        sec.Members!.SelectMany(s => s.ModuleIds)
            .Should().Equal("pitbox", "datastream", "leaderboard");
        G(layout, "mainHud").Members![^1].ModuleIds.Should().Equal("k10Logo", "carLogo");
    }

    [Fact]
    public void Centered_ApproximatesTheLegacyAbsoluteCenter()
    {
        var layout = Synthesize("centered");
        var main = G(layout, "mainHud");

        main.Anchor.Should().Be(LayoutAnchor.TopCenter);
        main.Locked.Should().BeFalse();
        main.X.Should().Be(0.5);
        main.Y.Should().BeApproximately(500.0 / H, 0.001, "legacy layout-ac pinned top:500px");
        // layout-ac main-area was row-reverse → reversed order.
        main.Members![0].ModuleIds.Should().Equal("k10Logo", "carLogo");

        // Centered derives secondary bottom-right, commentary bottom-left.
        G(layout, "secondary").Anchor.Should().Be(LayoutAnchor.BottomRight);
        G(layout, "commentary").Anchor.Should().Be(LayoutAnchor.BottomLeft);
    }

    [Fact]
    public void BottomYOffset_OnlyLiftsTheGameLogo_OnBottomLayouts()
    {
        var with = Synthesize("bottom-right", bottomYOffset: 144);
        G(with, "gameLogo").Y.Should().BeApproximately(0.985 - 144 / H, 0.0001);

        // Everything else is untouched by the offset…
        G(with, "secondary").Anchor.Should().Be(LayoutAnchor.TopRight);
        G(with, "mainHud").Locked.Should().BeTrue();

        // …and top layouts ignore it entirely (legacy gated on
        // pos.indexOf('bottom') === 0).
        var top = Synthesize("top-right", bottomYOffset: 144);
        G(top, "gameLogo").Y.Should().Be(0.985);
    }

    [Fact]
    public void EveryPosition_YieldsEditorReadyGroups()
    {
        foreach (var pos in new[] { "top-left", "top-right", "bottom-left", "bottom-right", "centered" })
        {
            var layout = Synthesize(pos);
            layout.Version.Should().Be(2);
            layout.Groups.Should().HaveCount(7, $"position {pos}");
            foreach (var g in layout.Groups!)
            {
                LayoutAnchor.IsValid(g.Anchor).Should().BeTrue($"{pos}/{g.Id}");
                g.X.Should().BeInRange(0, 1, $"{pos}/{g.Id}");
                g.Y.Should().BeInRange(0, 1, $"{pos}/{g.Id}");
            }
        }
    }
}
