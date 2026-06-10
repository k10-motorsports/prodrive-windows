using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RaceCorProDrive.Services
{
    /// <summary>
    /// The Visual-tab layout editor's brain — everything except pixels
    /// on screen. Holds the working copy of the v2 layout, computes
    /// proxy rectangles (metrics-first, registry nominals as fallback),
    /// applies gestures (move/snap/lock, extract, join, merge), and
    /// keeps an undo/redo stack. Deliberately WinUI-free so the test
    /// project compiles it directly; the SettingsPage partial is a thin
    /// pointer-event + rendering shell over this.
    ///
    /// Coordinates: all rect math is in SCREEN pixels of the target
    /// display (the same space the overlay renders in and the metrics
    /// sidecar reports in). The UI scales to canvas pixels at the edge.
    /// Contract + interaction spec: docs/overlay-layout-v2.md §3.
    /// </summary>
    public sealed class LayoutEditorModel
    {
        /// <summary>Visual edge gap for locked groups — the overlay keeps
        /// this constant across zoom via its --edge-z compensation.</summary>
        public const double EdgePx = 10;

        /// <summary>Snap radius around each anchor point, screen px.</summary>
        public const double SnapRadiusPx = 48;

        public OverlayLayout Layout { get; private set; }
        public ModuleRegistry Registry { get; }
        public OverlayMetrics? Metrics { get; set; }
        public double ScreenW { get; }
        public double ScreenH { get; }
        /// <summary>Overlay zoom percentage (165 == 165%).</summary>
        public double ZoomPercent { get; set; }

        /// <summary>Resolves a module's show* visibility (reads the live
        /// OverlaySettings). Hidden modules occupy no proxy space, same
        /// as display:none in the overlay's flex flow.</summary>
        public Func<RegistryModule, bool> IsModuleVisible { get; set; } = _ => true;

        private readonly List<string> _undo = new();
        private readonly List<string> _redo = new();
        private const int MaxUndo = 64;

        public LayoutEditorModel(
            OverlayLayout? layout, ModuleRegistry registry,
            double screenW, double screenH, double zoomPercent)
        {
            Registry = registry;
            ScreenW = screenW;
            ScreenH = screenH;
            ZoomPercent = zoomPercent;
            // Deep-clone whatever we start from: edits must not leak
            // into the caller's settings object until it asks ToLayout().
            Layout = Clone(layout ?? registry.DefaultLayout
                ?? throw new InvalidOperationException("registry has no defaultLayout"));
            Layout.Version = 2;
            Layout.Groups ??= new List<LayoutGroup>();
        }

        private double ZoomScale => (ZoomPercent <= 0 ? 100 : ZoomPercent) / 100.0;

        // ── Geometry ─────────────────────────────────────────────────

        public readonly record struct LayoutRect(double X, double Y, double W, double H)
        {
            public double Right => X + W;
            public double Bottom => Y + H;
            public bool Contains(double px, double py)
                => px >= X && px <= Right && py >= Y && py <= Bottom;
        }

        public sealed record ModuleProxy(string Id, string Label, LayoutRect Rect, bool Visible);
        public sealed record GroupProxy(LayoutGroup Group, LayoutRect Rect, IReadOnlyList<ModuleProxy> Modules);

        /// <summary>Measured size when the sidecar has one, else registry
        /// nominal scaled by zoom (nominals are pre-zoom CSS px).</summary>
        public (double W, double H) ModuleSize(string moduleId)
        {
            if (Metrics?.Modules != null && Metrics.Modules.TryGetValue(moduleId, out var m)
                && (m.W > 0 || m.H > 0))
                return (m.W, m.H);
            var reg = Registry.Find(moduleId);
            if (reg?.Nominal != null)
                return (reg.Nominal.W * ZoomScale, reg.Nominal.H * ZoomScale);
            return (80 * ZoomScale, 80 * ZoomScale);
        }

        /// <summary>
        /// Full proxy geometry for rendering and hit-testing: one rect
        /// per group, one per visible member module. Mirrors the
        /// overlay engine's flex flow (slots along the group flow,
        /// stacks perpendicular), so proxies match what renders even
        /// before the first metrics report; once metrics exist the
        /// module sizes are the measured ones.
        /// </summary>
        public IReadOnlyList<GroupProxy> ComputeProxies()
        {
            var result = new List<GroupProxy>();
            foreach (var group in Layout.Groups!)
            {
                if (group?.Id == null) continue;
                var gapPx = (group.Gap ?? 4) * ZoomScale;
                var flowRow = group.Flow != "column";

                // Slot sizes from visible members.
                var slots = new List<(List<(string Id, string Label, double W, double H, bool Vis)> Members, double W, double H)>();
                foreach (var slot in group.Members ?? new List<MemberSlot>())
                {
                    var members = new List<(string, string, double, double, bool)>();
                    foreach (var id in slot.ModuleIds)
                    {
                        var reg = Registry.Find(id);
                        if (reg == null) continue;
                        var vis = IsModuleVisible(reg);
                        var (w, h) = ModuleSize(id);
                        members.Add((id, reg.Label ?? id, w, h, vis));
                    }
                    var visMembers = members.Where(m => m.Item5).ToList();
                    if (members.Count == 0) continue;
                    // Stack flows perpendicular to the group.
                    double sw, sh;
                    if (flowRow)
                    {
                        sw = visMembers.Count == 0 ? 0 : visMembers.Max(m => m.Item3);
                        sh = visMembers.Sum(m => m.Item4) + Math.Max(0, visMembers.Count - 1) * gapPx;
                    }
                    else
                    {
                        sw = visMembers.Sum(m => m.Item3) + Math.Max(0, visMembers.Count - 1) * gapPx;
                        sh = visMembers.Count == 0 ? 0 : visMembers.Max(m => m.Item4);
                    }
                    slots.Add((members, sw, sh));
                }

                var occupied = slots.Where(s => s.W > 0 || s.H > 0).ToList();
                double gw, gh;
                if (flowRow)
                {
                    gw = occupied.Sum(s => s.W) + Math.Max(0, occupied.Count - 1) * gapPx;
                    gh = occupied.Count == 0 ? 0 : occupied.Max(s => s.H);
                }
                else
                {
                    gw = occupied.Count == 0 ? 0 : occupied.Max(s => s.W);
                    gh = occupied.Sum(s => s.H) + Math.Max(0, occupied.Count - 1) * gapPx;
                }

                // Prefer the measured group size when the sidecar has one
                // (pixel-exact decision); position is always computed so
                // drags predict correctly before the overlay re-measures.
                if (Metrics?.Groups != null && Metrics.Groups.TryGetValue(group.Id, out var mg)
                    && (mg.W > 0 || mg.H > 0))
                {
                    gw = mg.W; gh = mg.H;
                }

                var (tx, ty) = TargetPoint(group);
                var (pax, pay) = LayoutAnchor.Pivot(ValidAnchor(group.Anchor));
                var rect = new LayoutRect(tx - pax * gw, ty - pay * gh, gw, gh);

                // Member rects: walk the flow.
                var modules = new List<ModuleProxy>();
                double cursor = 0;
                foreach (var slot in occupied)
                {
                    double inner = 0;
                    foreach (var (id, label, w, h, vis) in slot.Members)
                    {
                        if (!vis) { modules.Add(new ModuleProxy(id, label, default, false)); continue; }
                        var mr = flowRow
                            ? new LayoutRect(rect.X + cursor, rect.Y + inner, w, h)
                            : new LayoutRect(rect.X + inner, rect.Y + cursor, w, h);
                        modules.Add(new ModuleProxy(id, label, mr, true));
                        inner += (flowRow ? h : w) + gapPx;
                    }
                    cursor += (flowRow ? slot.W : slot.H) + gapPx;
                }
                // Hidden modules from skipped slots still need rail rows.
                foreach (var slot in slots.Except(occupied))
                    foreach (var (id, label, _, _, _) in slot.Members)
                        modules.Add(new ModuleProxy(id, label, default, false));

                result.Add(new GroupProxy(group, rect, modules));
            }
            return result;
        }

        /// <summary>Where the group's anchor-pivot point sits on screen.</summary>
        public (double X, double Y) TargetPoint(LayoutGroup group)
        {
            var anchor = ValidAnchor(group.Anchor);
            var (pax, pay) = LayoutAnchor.Pivot(anchor);
            if (group.Locked == true)
            {
                var tx = pax * ScreenW + (pax == 0 ? EdgePx : pax == 1 ? -EdgePx : 0);
                var ty = pay * ScreenH + (pay == 0 ? EdgePx : pay == 1 ? -EdgePx : 0);
                return (tx, ty);
            }
            return ((group.X ?? 0) * ScreenW, (group.Y ?? 0) * ScreenH);
        }

        private static string ValidAnchor(string? anchor)
            => LayoutAnchor.IsValid(anchor) ? anchor! : LayoutAnchor.TopLeft;

        // ── Snap ─────────────────────────────────────────────────────

        public sealed record SnapResult(string Anchor, double X, double Y, double Distance);

        /// <summary>Anchor points in screen px (edge-inset, like locked targets).</summary>
        public IEnumerable<(string Anchor, double X, double Y)> AnchorPoints()
        {
            foreach (var a in LayoutAnchor.All)
            {
                var (px, py) = LayoutAnchor.Pivot(a);
                yield return (a,
                    px * ScreenW + (px == 0 ? EdgePx : px == 1 ? -EdgePx : 0),
                    py * ScreenH + (py == 0 ? EdgePx : py == 1 ? -EdgePx : 0));
            }
        }

        /// <summary>Nearest anchor; null when outside the snap radius.</summary>
        public SnapResult? HitTestSnap(double px, double py)
        {
            var best = AnchorPoints()
                .Select(a => new SnapResult(a.Anchor, a.X, a.Y,
                    Math.Sqrt((a.X - px) * (a.X - px) + (a.Y - py) * (a.Y - py))))
                .OrderBy(r => r.Distance)
                .First();
            return best.Distance <= SnapRadiusPx ? best : null;
        }

        public string NearestAnchor(double px, double py)
            => AnchorPoints()
                .OrderBy(a => (a.X - px) * (a.X - px) + (a.Y - py) * (a.Y - py))
                .First().Anchor;

        // ── Gestures ─────────────────────────────────────────────────

        /// <summary>
        /// Commit a group drag. Inside an anchor's snap radius → locked
        /// to that anchor; otherwise free-hand with the anchor re-pointed
        /// at the nearest 9-point so the pivot stays sensible.
        /// (px,py) is where the group's CURRENT pivot landed.
        /// </summary>
        public void CommitGroupDrag(string groupId, double px, double py)
        {
            var group = Require(groupId);
            PushUndo();
            var snap = HitTestSnap(px, py);
            if (snap != null)
            {
                group.Anchor = snap.Anchor;
                group.Locked = true;
                var (pax, pay) = LayoutAnchor.Pivot(snap.Anchor);
                group.X = pax; // retained for unlock-restore
                group.Y = pay;
            }
            else
            {
                group.Anchor = NearestAnchor(px, py);
                group.Locked = false;
                group.X = Clamp01(px / ScreenW);
                group.Y = Clamp01(py / ScreenH);
            }
        }

        public void SetAnchor(string groupId, string anchor)
        {
            if (!LayoutAnchor.IsValid(anchor)) throw new ArgumentException($"bad anchor '{anchor}'");
            var group = Require(groupId);
            PushUndo();
            group.Anchor = anchor;
            if (group.Locked == true)
            {
                var (pax, pay) = LayoutAnchor.Pivot(anchor);
                group.X = pax; group.Y = pay;
            }
        }

        public void SetLocked(string groupId, bool locked)
        {
            var group = Require(groupId);
            PushUndo();
            group.Locked = locked;
            if (locked)
            {
                var (pax, pay) = LayoutAnchor.Pivot(ValidAnchor(group.Anchor));
                group.X = pax; group.Y = pay;
            }
        }

        public void SetFlow(string groupId, string flow)
        {
            if (flow is not ("row" or "column")) throw new ArgumentException($"bad flow '{flow}'");
            var group = Require(groupId);
            PushUndo();
            group.Flow = flow;
        }

        public void SetGap(string groupId, double gap)
        {
            var group = Require(groupId);
            // Coalesced: a slider drag fires this per tick — one undo
            // entry for the whole drag, not one per pixel.
            PushUndo($"gap:{groupId}");
            group.Gap = Math.Max(0, gap);
        }

        public void Rename(string groupId, string label)
        {
            var group = Require(groupId);
            PushUndo();
            group.Label = string.IsNullOrWhiteSpace(label) ? group.Id : label.Trim();
        }

        /// <summary>
        /// Break a module out of its group into a new single-member
        /// group dropped free-hand at (px,py). Returns the new group id.
        /// </summary>
        public string ExtractModule(string moduleId, double px, double py)
        {
            PushUndo();
            RemoveFromCurrentGroup(moduleId);

            var id = UniqueGroupId(moduleId);
            var reg = Registry.Find(moduleId);
            var anchor = NearestAnchor(px, py);
            Layout.Groups!.Add(new LayoutGroup
            {
                Id = id,
                Label = reg?.Label ?? moduleId,
                Anchor = anchor,
                X = Clamp01(px / ScreenW),
                Y = Clamp01(py / ScreenH),
                Locked = false,
                Flow = "row",
                Gap = 4,
                Members = new List<MemberSlot> { new MemberSlot(moduleId) },
            });
            return id;
        }

        /// <summary>Join a module to another group (appended as its own slot).</summary>
        public void MoveModuleToGroup(string moduleId, string targetGroupId)
        {
            var target = Require(targetGroupId);
            if (target.Members != null
                && target.Members.Any(s => s.ModuleIds.Contains(moduleId))) return; // already there
            PushUndo();
            RemoveFromCurrentGroup(moduleId);
            target.Members ??= new List<MemberSlot>();
            target.Members.Add(new MemberSlot(moduleId));
        }

        /// <summary>
        /// Merge: first id is the surviving group (keeps its position);
        /// the others' slots are appended in selection order.
        /// </summary>
        public string MergeGroups(IReadOnlyList<string> groupIds)
        {
            if (groupIds.Count < 2) throw new ArgumentException("merge needs at least two groups");
            var target = Require(groupIds[0]);
            PushUndo();
            target.Members ??= new List<MemberSlot>();
            foreach (var otherId in groupIds.Skip(1))
            {
                var other = Require(otherId);
                if (other.Members != null) target.Members.AddRange(other.Members);
                Layout.Groups!.Remove(other);
            }
            return target.Id!;
        }

        /// <summary>Dissolve a group; members fall back to their registry
        /// default groups (created at default position if absent).</summary>
        public void DeleteGroup(string groupId)
        {
            var group = Require(groupId);
            PushUndo();
            var members = (group.Members ?? new List<MemberSlot>())
                .SelectMany(s => s.ModuleIds).ToList();
            Layout.Groups!.Remove(group);
            foreach (var moduleId in members)
            {
                var homeId = Registry.DefaultGroupIdFor(moduleId);
                if (homeId == null || homeId == groupId) continue;
                var home = Layout.Groups!.FirstOrDefault(g => g.Id == homeId);
                if (home == null)
                {
                    var seed = Registry.DefaultLayout?.Groups?.FirstOrDefault(g => g.Id == homeId);
                    if (seed == null) continue;
                    home = Clone(seed);
                    home.Members = new List<MemberSlot>();
                    Layout.Groups!.Add(home);
                }
                home.Members ??= new List<MemberSlot>();
                if (!home.Members.Any(s => s.ModuleIds.Contains(moduleId)))
                    home.Members.Add(new MemberSlot(moduleId));
            }
        }

        public void ResetToDefault()
        {
            PushUndo();
            Layout = Clone(Registry.DefaultLayout!);
            Layout.Version = 2;
        }

        // ── Undo / redo ──────────────────────────────────────────────

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            _redo.Add(Serialize(Layout));
            Layout = Deserialize(_undo[^1]);
            _undo.RemoveAt(_undo.Count - 1);
            _lastUndoTag = null;
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            _undo.Add(Serialize(Layout));
            Layout = Deserialize(_redo[^1]);
            _redo.RemoveAt(_redo.Count - 1);
            _lastUndoTag = null;
            return true;
        }

        private string? _lastUndoTag;

        /// <summary>Snapshot for undo. A non-null tag matching the
        /// previous push coalesces (continuous-gesture ops like a gap
        /// slider produce one undo step, not one per tick).</summary>
        private void PushUndo(string? tag = null)
        {
            if (tag != null && tag == _lastUndoTag) return;
            _undo.Add(Serialize(Layout));
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
            _lastUndoTag = tag;
        }

        // ── Output ───────────────────────────────────────────────────

        /// <summary>The working layout, for writing into OverlaySettings.Layout.</summary>
        public OverlayLayout ToLayout() => Layout;

        /// <summary>
        /// Best-effort legacy layoutPosition for older overlay binaries
        /// (dual-write during the transition): the corner the group
        /// holding the tacho is anchored to, else top-right.
        /// </summary>
        public string DeriveLegacyPosition()
        {
            var main = Layout.Groups!
                .FirstOrDefault(g => g.Members != null
                    && g.Members.Any(s => s.ModuleIds.Contains("tacho")))
                ?? Layout.Groups!.FirstOrDefault();
            return main?.Anchor switch
            {
                LayoutAnchor.TopLeft => "top-left",
                LayoutAnchor.TopRight => "top-right",
                LayoutAnchor.BottomLeft => "bottom-left",
                LayoutAnchor.BottomRight => "bottom-right",
                LayoutAnchor.Center => "centered",
                _ => "top-right",
            };
        }

        // ── Internals ────────────────────────────────────────────────

        private LayoutGroup Require(string groupId)
            => Layout.Groups!.FirstOrDefault(g => g.Id == groupId)
               ?? throw new ArgumentException($"unknown group '{groupId}'");

        /// <summary>Remove a module wherever it lives; prune emptied slots
        /// and emptied groups (a groupless module would be re-homed by
        /// the overlay's defaultGroup fallback, but the editor never
        /// produces that state).</summary>
        private void RemoveFromCurrentGroup(string moduleId)
        {
            foreach (var group in Layout.Groups!.ToList())
            {
                if (group.Members == null) continue;
                foreach (var slot in group.Members.ToList())
                {
                    if (!slot.ModuleIds.Remove(moduleId)) continue;
                    if (slot.ModuleIds.Count == 0) group.Members.Remove(slot);
                    if (group.Members.Count == 0) Layout.Groups!.Remove(group);
                    return;
                }
            }
        }

        private string UniqueGroupId(string baseId)
        {
            var id = baseId;
            var n = 2;
            while (Layout.Groups!.Any(g => g.Id == id)) id = $"{baseId}-{n++}";
            return id;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        private static readonly JsonSerializerOptions CloneOpts = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private static string Serialize(OverlayLayout layout)
            => JsonSerializer.Serialize(layout, CloneOpts);

        private static OverlayLayout Deserialize(string json)
            => JsonSerializer.Deserialize<OverlayLayout>(json, CloneOpts)!;

        private static OverlayLayout Clone(OverlayLayout layout)
            => Deserialize(Serialize(layout));

        private static LayoutGroup Clone(LayoutGroup group)
            => JsonSerializer.Deserialize<LayoutGroup>(
                JsonSerializer.Serialize(group, CloneOpts), CloneOpts)!;
    }
}
