using System;
using System.Collections.Generic;
using System.Linq;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// Phase 3 migration: synthesize a v2 layout from the legacy keys
    /// (<c>layoutPosition</c> / <c>bottomYOffset</c>) so a user opening
    /// the editor for the first time sees the arrangement they already
    /// have, not the factory default. Faithful port of the overlay's
    /// legacy derivation (settings.js applyLayout + dashboard.css flex
    /// rules), including its quirks:
    ///
    ///  - main-area runs row-reverse for LEFT corners and centered, so
    ///    the visual module order flips (logos hug the anchored edge);
    ///  - sec-container runs row-reverse for RIGHT corners, keeping the
    ///    leaderboard at the screen edge;
    ///  - secondary panels take the opposite vertical / same horizontal
    ///    edge as the main HUD; incidents the same vertical as secondary
    ///    but opposite horizontal; commentary the diagonal corner;
    ///  - bottomYOffset only ever visibly moved the game logo (it was
    ///    applied as margin-bottom to elements that sit at the TOP in
    ///    bottom layouts), so that is all it synthesizes to;
    ///  - "centered" (layout-ac) put the dashboard at top:500px,
    ///    horizontally centered — approximated as a free top-center
    ///    group at y = 500 / screen height.
    ///
    /// Synthesis is lazy: the editor seeds from it when settings carry
    /// no layout block; nothing is written until the first gesture.
    /// The proactive write + legacy-key deletion is Phase 3b (cutover,
    /// one shipped release later). docs/overlay-layout-v2.md §4.
    /// </summary>
    public static class LayoutSynthesis
    {
        public static OverlayLayout FromLegacy(
            OverlaySettings settings, ModuleRegistry registry,
            double screenW, double screenH)
        {
            var pos = settings.LayoutPosition ?? "top-right";
            var isCenter = pos is "centered" or "absolute-center";
            var isBottom = !isCenter && pos.Contains("bottom", StringComparison.Ordinal);
            var isRight = !isCenter && pos.Contains("right", StringComparison.Ordinal);
            var yOff = settings.BottomYOffset ?? 0;

            // ── Main HUD ────────────────────────────────────────────
            // Visual member order is always left→right in v2; legacy
            // right corners rendered the DOM order (row), left corners
            // and centered reversed it (row-reverse).
            var mainSlots = new List<MemberSlot>
            {
                new("controls", "pedals"),
                new("maps"),
                new("position", "gaps"),
                new("tacho"),
                new("k10Logo", "carLogo"),
            };
            if (!isRight) mainSlots.Reverse();

            LayoutGroup mainHud;
            if (isCenter)
            {
                mainHud = Group("mainHud", "Main HUD", LayoutAnchor.TopCenter,
                    x: 0.5, y: Math.Round(500.0 / screenH, 4), locked: false, mainSlots);
            }
            else
            {
                var anchor = Corner(isBottom, isRight);
                mainHud = Group("mainHud", "Main HUD", anchor,
                    PivotX(anchor), PivotY(anchor), locked: true, mainSlots);
            }

            // ── Race timer — rode directly under the main row ───────
            var timerAnchor = isCenter ? LayoutAnchor.TopCenter : Corner(isBottom, isRight);
            var timer = Group("timer", "Race timer", timerAnchor,
                x: isCenter ? 0.5 : (isRight ? 0.935 : 0.065),
                y: isCenter ? Math.Round(500.0 / screenH + 0.14, 4) : (isBottom ? 0.825 : 0.175),
                locked: false,
                new List<MemberSlot> { new("timer") });

            // ── Secondary panels — opposite vertical, same horizontal ─
            var secBottom = isCenter || !isBottom;
            var secRight = isCenter || isRight;
            var secondaryMembers = secRight
                ? new List<MemberSlot> { new("pitbox"), new("datastream"), new("leaderboard") }
                : new List<MemberSlot> { new("leaderboard"), new("datastream"), new("pitbox") };
            var secAnchor = Corner(secBottom, secRight);
            var secondary = Group("secondary", "Race data", secAnchor,
                PivotX(secAnchor), PivotY(secAnchor), locked: true, secondaryMembers);

            // ── Incidents — secondary's vertical, opposite horizontal ─
            var incAnchor = Corner(secBottom, !secRight);
            var incidents = Group("incidents", "Incidents", incAnchor,
                PivotX(incAnchor), PivotY(incAnchor), locked: true,
                new List<MemberSlot> { new("incidents") });

            // ── Commentary — diagonal from the main HUD ─────────────
            var cmtTop = !isCenter && isBottom;     // centered → bottom-left
            var cmtLeft = isCenter || isRight;
            var commentary = Group("commentary", "Commentary",
                Corner(!cmtTop, !cmtLeft),
                x: cmtLeft ? 0.012 : 0.988,
                y: cmtTop ? 0.3 : 0.7,
                locked: false,
                new List<MemberSlot> { new("commentary") },
                flow: "column"); // matches the canonical seed; moot for one member

            // ── Spotter — always the fixed bottom-center overlay ────
            var spotter = Group("spotter", "Spotter", LayoutAnchor.BottomCenter,
                0.5, 1, locked: true, new List<MemberSlot> { new("spotter") });

            // ── Game logo — bottom-left; the only element the legacy
            //    bottomYOffset visibly moved (bottom layouts only) ────
            var logoY = 0.985;
            if (isBottom && yOff > 0)
                logoY = Math.Max(0, Math.Round(logoY - yOff / screenH, 4));
            var gameLogo = Group("gameLogo", "Game logo", LayoutAnchor.BottomLeft,
                0.06, logoY, locked: false, new List<MemberSlot> { new("gameLogo") });

            var layout = new OverlayLayout
            {
                Version = 2,
                Groups = new List<LayoutGroup>
                    { mainHud, timer, secondary, incidents, commentary, spotter, gameLogo },
            };

            // Drop modules the registry doesn't know (defensive: a
            // future slimmer registry shouldn't make synthesis emit
            // members the engine can't resolve).
            var known = registry.Modules!.Select(m => m.Id).ToHashSet();
            foreach (var group in layout.Groups)
            {
                group.Members = group.Members!
                    .Select(s => new MemberSlot(s.ModuleIds.Where(id => known.Contains(id)).ToArray()))
                    .Where(s => s.ModuleIds.Count > 0)
                    .ToList();
            }
            layout.Groups.RemoveAll(g => g.Members!.Count == 0);
            return layout;
        }

        private static LayoutGroup Group(
            string id, string label, string anchor,
            double x, double y, bool locked, List<MemberSlot> members,
            string flow = "row") => new()
        {
            Id = id,
            Label = label,
            Anchor = anchor,
            X = x,
            Y = y,
            Locked = locked,
            Flow = flow,
            Gap = 4,
            Members = members,
        };

        private static string Corner(bool bottom, bool right) => (bottom, right) switch
        {
            (false, false) => LayoutAnchor.TopLeft,
            (false, true)  => LayoutAnchor.TopRight,
            (true,  false) => LayoutAnchor.BottomLeft,
            (true,  true)  => LayoutAnchor.BottomRight,
        };

        private static double PivotX(string anchor) => LayoutAnchor.Pivot(anchor).Px;
        private static double PivotY(string anchor) => LayoutAnchor.Pivot(anchor).Py;
    }
}
