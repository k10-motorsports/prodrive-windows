using FluentAssertions;
using RaceCorProDrive.Services;
using Xunit;

namespace RaceCorProDrive.Tests.Services;

/// <summary>
/// LayoutEditorModel — the Visual tab editor's logic layer. These run
/// against the real embedded registry (same bytes the overlay ships),
/// a 2560×1440 virtual screen, and the overlay's default 165% zoom,
/// so the numbers mirror a real install. The WinUI partial on top of
/// this model is intentionally too thin to need tests (CI compiles it).
/// </summary>
public class LayoutEditorModelTests
{
    private const double W = 2560, H = 1440;

    private static LayoutEditorModel NewModel(OverlayLayout? layout = null, double zoom = 165)
        => new(layout, ModuleRegistry.LoadEmbedded(), W, H, zoom);

    // ── Seeding ──────────────────────────────────────────────────────

    [Fact]
    public void NullLayout_SeedsFromRegistryDefault_WithoutAliasingIt()
    {
        var model = NewModel();
        model.Layout.Groups.Should().HaveCount(7);

        model.Rename("mainHud", "Renamed");
        ModuleRegistry.LoadEmbedded().DefaultLayout!.Groups!
            .Single(g => g.Id == "mainHud").Label.Should().Be("Main HUD",
                "the model must deep-clone its seed, not mutate the registry");
    }

    // ── Proxy geometry ───────────────────────────────────────────────

    [Fact]
    public void LockedTopRightGroup_PinsToCornerWithEdgeInset()
    {
        var proxies = NewModel().ComputeProxies();
        var main = proxies.Single(p => p.Group.Id == "mainHud");

        main.Rect.Right.Should().BeApproximately(W - LayoutEditorModel.EdgePx, 0.001);
        main.Rect.Y.Should().BeApproximately(LayoutEditorModel.EdgePx, 0.001);
        main.Rect.W.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FreeGroup_PlacesPivotAtNormalizedPoint()
    {
        // commentary: free, anchor bottom-left, x=0.012, y=0.7.
        var proxies = NewModel().ComputeProxies();
        var cmt = proxies.Single(p => p.Group.Id == "commentary");

        cmt.Rect.X.Should().BeApproximately(0.012 * W, 0.001);
        cmt.Rect.Bottom.Should().BeApproximately(0.7 * H, 0.001);
    }

    [Fact]
    public void StackSlot_FlowsPerpendicular_AndScalesWithZoom()
    {
        var model = NewModel(zoom: 200); // 2× nominals, easy math
        var main = model.ComputeProxies().Single(p => p.Group.Id == "mainHud");

        var controls = main.Modules.Single(m => m.Id == "controls");
        var pedals = main.Modules.Single(m => m.Id == "pedals");

        // Same column (stack in a row group), pedals below controls.
        controls.Rect.X.Should().BeApproximately(pedals.Rect.X, 0.001);
        pedals.Rect.Y.Should().BeGreaterThan(controls.Rect.Y);

        // nominal 120×98 at 200% → 240×196.
        controls.Rect.W.Should().BeApproximately(240, 0.001);
        controls.Rect.H.Should().BeApproximately(196, 0.001);
    }

    [Fact]
    public void Metrics_OverrideNominalEstimates()
    {
        var model = NewModel();
        model.Metrics = new OverlayMetrics
        {
            Version = 2,
            Modules = new() { ["tacho"] = new MetricsSize { W = 333, H = 444 } },
            Groups = new() { ["mainHud"] = new MetricsRect { X = 1, Y = 2, W = 1500, H = 400 } },
        };

        var main = model.ComputeProxies().Single(p => p.Group.Id == "mainHud");
        main.Rect.W.Should().Be(1500, "measured group size wins");
        main.Rect.Right.Should().BeApproximately(W - LayoutEditorModel.EdgePx, 0.001,
            "position is still computed from the anchor, not the stale measured x/y");
        main.Modules.Single(m => m.Id == "tacho").Rect.W.Should().Be(333);
    }

    [Fact]
    public void HiddenModule_OccupiesNoSpace_ButStaysListed()
    {
        var model = NewModel();
        var fullWidth = model.ComputeProxies().Single(p => p.Group.Id == "secondary").Rect.W;

        model.IsModuleVisible = m => m.Id != "datastream";
        var proxies = model.ComputeProxies().Single(p => p.Group.Id == "secondary");

        proxies.Rect.W.Should().BeLessThan(fullWidth);
        proxies.Modules.Single(m => m.Id == "datastream").Visible.Should().BeFalse();
    }

    // ── Snap + drag ──────────────────────────────────────────────────

    [Fact]
    public void HitTestSnap_InsideRadius_FindsAnchor_OutsideReturnsNull()
    {
        var model = NewModel();
        model.HitTestSnap(W - 30, 30)!.Anchor.Should().Be(LayoutAnchor.TopRight);
        model.HitTestSnap(W / 2 + 200, H / 2 + 200).Should().BeNull();
    }

    [Fact]
    public void CommitGroupDrag_NearCorner_LocksToIt()
    {
        var model = NewModel();
        model.CommitGroupDrag("commentary", 25, H - 25);

        var g = model.Layout.Groups!.Single(g => g.Id == "commentary");
        g.Locked.Should().BeTrue();
        g.Anchor.Should().Be(LayoutAnchor.BottomLeft);
    }

    [Fact]
    public void CommitGroupDrag_FreeDrop_StoresNormalizedPivot()
    {
        var model = NewModel();
        model.CommitGroupDrag("mainHud", 0.3 * W, 0.4 * H);

        var g = model.Layout.Groups!.Single(g => g.Id == "mainHud");
        g.Locked.Should().BeFalse();
        g.X.Should().BeApproximately(0.3, 0.0001);
        g.Y.Should().BeApproximately(0.4, 0.0001);
        LayoutAnchor.IsValid(g.Anchor).Should().BeTrue();
    }

    // ── Membership gestures ──────────────────────────────────────────

    [Fact]
    public void ExtractModule_SplitsIntoNewFreeGroup()
    {
        var model = NewModel();
        var newId = model.ExtractModule("tacho", 0.5 * W, 0.5 * H);

        newId.Should().Be("tacho");
        var main = model.Layout.Groups!.Single(g => g.Id == "mainHud");
        main.Members!.SelectMany(s => s.ModuleIds).Should().NotContain("tacho");
        var extracted = model.Layout.Groups!.Single(g => g.Id == "tacho");
        extracted.Locked.Should().BeFalse();
        extracted.Members!.Single().ModuleIds.Should().Equal("tacho");
    }

    [Fact]
    public void ExtractModule_FromStack_PrunesSlotNotSiblings()
    {
        var model = NewModel();
        model.ExtractModule("controls", 100, 100);

        var main = model.Layout.Groups!.Single(g => g.Id == "mainHud");
        var ids = main.Members!.SelectMany(s => s.ModuleIds).ToList();
        ids.Should().NotContain("controls");
        ids.Should().Contain("pedals", "the stack's other member stays put");
    }

    [Fact]
    public void ExtractLastMember_RemovesTheEmptiedGroup()
    {
        var model = NewModel();
        model.ExtractModule("spotter", 100, 100); // spotter group's only member

        model.Layout.Groups!.Count(g => g.Members!.SelectMany(s => s.ModuleIds).Contains("spotter"))
            .Should().Be(1, "exactly the new group");
        model.Layout.Groups!.Should().HaveCount(7, "old emptied group removed, new one added");
    }

    [Fact]
    public void MoveModuleToGroup_AppendsSlot_AndIsIdempotent()
    {
        var model = NewModel();
        model.MoveModuleToGroup("datastream", "incidents");
        model.MoveModuleToGroup("datastream", "incidents");

        var inc = model.Layout.Groups!.Single(g => g.Id == "incidents");
        inc.Members!.SelectMany(s => s.ModuleIds).Count(id => id == "datastream").Should().Be(1);
        model.Layout.Groups!.Single(g => g.Id == "secondary")
            .Members!.SelectMany(s => s.ModuleIds).Should().NotContain("datastream");
    }

    [Fact]
    public void MergeGroups_FirstSurvives_OthersFoldIn()
    {
        var model = NewModel();
        var survivor = model.MergeGroups(new[] { "secondary", "incidents", "gameLogo" });

        survivor.Should().Be("secondary");
        model.Layout.Groups!.Should().NotContain(g => g.Id == "incidents" || g.Id == "gameLogo");
        model.Layout.Groups!.Single(g => g.Id == "secondary")
            .Members!.SelectMany(s => s.ModuleIds)
            .Should().Contain(new[] { "leaderboard", "datastream", "pitbox", "incidents", "gameLogo" });
    }

    [Fact]
    public void DeleteGroup_SendsMembersToTheirDefaultHomes()
    {
        var model = NewModel();
        model.ExtractModule("tacho", 100, 100);
        model.DeleteGroup("tacho");

        model.Layout.Groups!.Single(g => g.Id == "mainHud")
            .Members!.SelectMany(s => s.ModuleIds).Should().Contain("tacho");
        model.Layout.Groups!.Should().NotContain(g => g.Id == "tacho");
    }

    // ── Undo / redo ──────────────────────────────────────────────────

    [Fact]
    public void UndoRedo_RoundTripsAGesture()
    {
        var model = NewModel();
        model.CommitGroupDrag("mainHud", 0.3 * W, 0.4 * H);

        model.Undo().Should().BeTrue();
        var g = model.Layout.Groups!.Single(g => g.Id == "mainHud");
        g.Locked.Should().BeTrue("back to the seed state");
        g.Anchor.Should().Be(LayoutAnchor.TopRight);

        model.Redo().Should().BeTrue();
        model.Layout.Groups!.Single(g => g.Id == "mainHud").Locked.Should().BeFalse();
    }

    [Fact]
    public void SetGap_CoalescesConsecutiveTicks_IntoOneUndoStep()
    {
        var model = NewModel();
        model.SetGap("mainHud", 6);
        model.SetGap("mainHud", 8);
        model.SetGap("mainHud", 12);

        model.Undo().Should().BeTrue();
        model.Layout.Groups!.Single(g => g.Id == "mainHud").Gap.Should().Be(4,
            "one undo step covers the whole slider drag");
        model.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void NewGestureAfterUndo_ClearsRedo()
    {
        var model = NewModel();
        model.SetFlow("secondary", "column");
        model.Undo();
        model.SetFlow("secondary", "column");
        model.Redo().Should().BeFalse();
    }

    // ── Output ───────────────────────────────────────────────────────

    [Fact]
    public void DeriveLegacyPosition_FollowsTheTachoGroupAnchor()
    {
        var model = NewModel();
        model.DeriveLegacyPosition().Should().Be("top-right");

        model.CommitGroupDrag("mainHud", 25, H - 25); // locks bottom-left
        model.DeriveLegacyPosition().Should().Be("bottom-left");

        model.SetAnchor("mainHud", LayoutAnchor.MiddleLeft);
        model.DeriveLegacyPosition().Should().Be("top-right", "non-corner anchors fall back");
    }

    [Fact]
    public void ToLayout_IsVersion2_AndSerializesSlotForms()
    {
        var model = NewModel();
        var json = System.Text.Json.JsonSerializer.Serialize(model.ToLayout());

        model.ToLayout().Version.Should().Be(2);
        json.Should().Contain("[\"controls\",\"pedals\"]", "stacks stay arrays");
        json.Should().Contain("\"maps\"", "singles stay strings");
    }
}

public class OverlayMetricsServiceTests
{
    [Fact]
    public void Parse_ReadsTheSidecarShape()
    {
        const string json = """
            { "version": 2, "viewport": { "w": 2560, "h": 1440 }, "zoom": 1.65,
              "modules": { "tacho": { "w": 248, "h": 330 } },
              "groups": { "mainHud": { "x": 1284, "y": 13, "w": 1263, "h": 330 } } }
            """;
        var m = OverlayMetricsService.Parse(json)!;
        m.Version.Should().Be(2);
        m.Viewport!.W.Should().Be(2560);
        m.Zoom.Should().Be(1.65);
        m.Modules!["tacho"].H.Should().Be(330);
        m.Groups!["mainHud"].W.Should().Be(1263);
    }
}
