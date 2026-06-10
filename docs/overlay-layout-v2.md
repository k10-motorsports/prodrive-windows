# Overlay Layout v2 — Preview-Based Module Placement

**Status:** planned · **Owners:** prodrive-windows (host editor) + prodrive-overlay (engine rewrite)
**Decisions locked 2026-06-10**, refined same day (see [Decision log](#decision-log)).

## Vision

Replace the overlay's implicit corner-derived layout with explicit, per-group placement
data, edited visually in the Windows host:

- The Settings → **Visual** tab becomes a **live screen preview** — a scaled capture of
  the target display with **proxy boxes** (module name labels) drawn on top.
- The user drags proxies to move modules, clicks/drags to **group or break apart**
  modules, and toggles individual modules on/off.
- Placement per group is **free-hand** (drop anywhere) or **corner-locked** (snap to one
  of 9 anchor points).
- The host edits **by proxy only** — it writes layout data to `overlay-settings.json`;
  the real overlay re-renders from data via its existing `settings-sync` pipeline. The
  host never touches overlay DOM. If the overlay is running while editing, it follows
  live on each save.
- The overlay's layout engine is **rewritten to be trivially data-driven**: read groups,
  position group containers, reparent member modules into them. All derived-placement
  magic dies.

## Why

Today (`prodrive-overlay/modules/js/settings.js` `applyLayout`, lines 27–108 +
`dashboard.css` lines 15–54) the whole dashboard pins to one of 5 corners and everything
else is *derived*: main-row flex direction flips per corner, secondary panels
(leaderboard/datastream/pitbox) jump to the opposite vertical edge, incidents take the
opposite horizontal edge from secondary (with inline-style forcing to dodge stale CEF
class state), commentary goes diagonally opposite, `bottomYOffset` patches bottom
layouts via margins. No per-module placement, lots of implicit rules, and the host UI
can only offer a 5-way radio. v2 makes placement explicit data so the renderer is
trivial and the editor is unconstrained.

---

## 1. Data model

### 1.1 `layout` block (in `overlay-settings.json`)

Written by the host, read by the overlay. Lives alongside existing keys.

```jsonc
"layout": {
  "version": 2,
  "groups": [
    {
      "id": "mainHud",            // stable id; default groups have well-known ids
      "label": "Main HUD",        // user-editable display name
      "anchor": "top-right",      // 9-point: top-left|top-center|top-right|middle-left|
                                  //          center|middle-right|bottom-left|bottom-center|bottom-right
      "x": 1.0, "y": 0.0,         // normalized 0..1 position of the anchor point on the work area
      "locked": true,             // true → pinned exactly to the anchor (with --edge-z inset);
                                  //        x,y retained for restore-on-unlock
      "flow": "row",              // row | column — member flow inside the group
      "gap": 4,                   // px between members (pre-zoom CSS px)
      // Member SLOTS: a string is one module; an array is a stack that
      // flows perpendicular to the group (one nesting level only).
      // Stacks are how the main HUD keeps controls-over-pedals,
      // position-over-gaps and the two logo squares as vertical pairs
      // inside a row group — without them the default layout could not
      // reproduce today's look.
      "members": [["controls", "pedals"], "maps", ["position", "gaps"], "tacho", ["k10Logo", "carLogo"]]
    },
    { "id": "secondary", "label": "Race data", "anchor": "bottom-right",
      "x": 1.0, "y": 1.0, "locked": true, "flow": "row", "gap": 4,
      "members": ["leaderboard", "datastream", "pitbox"] }
    // ... timer, incidents, commentary, spotter, gameLogo
  ]
}
```

Semantics:

- **anchor** is dual-purpose: the snap target on screen *and* the group's own pivot.
  A `top-right` group positions its own top-right corner at `(x·vw, y·vh)`.
- **locked: true** → engine ignores `x,y` and pins to the anchor's natural point inset
  by the zoom-compensated edge gap (`--edge-z`). This is "choose a corner to lock to."
- **locked: false** → free-hand; engine places the anchor-pivot at `(x·vw, y·vh)`.
  Normalized coords make free-hand placements resolution-independent.
- **Groups are membership-only** (locked decision): members co-locate and flow/move
  together. There is **no inter-group docking** — "lock to each other" == same group.
- **Visibility stays in the existing `show*` keys** (per module). A module absent from
  every group falls back to its registry `defaultGroup`. A hidden module keeps its
  membership; the engine just skips it in flow.

### 1.2 Module registry (single source of truth)

The overlay owns module identity (ids ↔ DOM selectors). A canonical
`layout-registry.json` is checked into **prodrive-overlay** and shipped as an
electron-builder `extraResources` entry so it lands at `Overlay/resources/layout-registry.json`
— a plain file (outside the asar) the host reads at runtime via the path
`OverlayLauncher.ResolveBinary()` already locates. The host keeps an embedded fallback
copy (for dev runs without a built overlay) plus a fixture test that fails if the two drift.

```jsonc
{
  "version": 2,
  "modules": [
    { "id": "tacho",       "label": "Tachometer",  "selector": ".tacho-block",
      "showKey": "showTacho",       "nominal": { "w": 150, "h": 200 }, "placeable": true },
    { "id": "leaderboard", "label": "Leaderboard", "selector": "#leaderboardPanel",
      "showKey": "showLeaderboard", "nominal": { "w": 320, "h": 240 }, "placeable": true },
    { "id": "raceControl", "label": "Race control banner", "selector": "#rcBanner",
      "showKey": null, "nominal": null, "placeable": false }
    // ... full inventory
  ],
  // The canonical seed layout (same shape as the settings `layout`
  // block). A module's "default group" is derived by looking it up
  // here rather than stored per-module — one source, no drift.
  "defaultLayout": { "version": 2, "groups": [ /* …seed groups… */ ] }
}
```

- `nominal` sizes are the **fallback only** — used by the editor until the overlay has
  run once and reported real measurements (§1.3).
- `placeable: false` marks system overlays (race control banner, pit limiter, race-end
  screen) that auto-show and are excluded from the editor in v1. Drive HUD mode is
  entirely out of scope — it bypasses this system today and continues to.

### 1.3 Metrics sidecar (`overlay-metrics.json`)

Pixel-exact proxies (locked decision) need real rendered sizes. The overlay writes a
**separate sidecar file** in the same directory as the settings file:

```jsonc
{
  "version": 2,
  "viewport": { "w": 2560, "h": 1440 },
  "zoom": 1.65,
  "modules": { "tacho": { "w": 248, "h": 330 }, "leaderboard": { "w": 528, "h": 396 } },
  "groups":  { "mainHud": { "x": 1284, "y": 13, "w": 1263, "h": 330 } }
}
```

All values are **visual px** (post-zoom `getBoundingClientRect`). The renderer measures
after each layout apply + on `ResizeObserver` ticks, debounced ~500 ms, and asks the
main process (new IPC `report-layout-metrics`) to write the file atomically. The host
watches it with a second `FileSystemWatcher` and re-scales proxies live.

> **Refinement of the locked "metrics into the settings file" decision:** metrics get a
> *sidecar*, not keys inside `overlay-settings.json`. Both processes serialize whole
> files on save — two writers to one file means last-writer-wins clobbering (host
> `SaveAsync` during an overlay metrics write would silently drop one side). One writer
> per file: settings = desired state (host writes), metrics = observed state (overlay
> writes). The intent of the decision (pixel-exact proxies fed by measured sizes) is
> unchanged.

---

## 2. Overlay engine rewrite (prodrive-overlay)

New `modules/js/layout-v2.js`, gated on `settings.layout?.version === 2`:

1. **Build group containers** — one `position: fixed` div per group
   (`.layout-group[data-group-id]`), `display: flex` with `flow`/`gap`.
2. **Reparent members** — move each member's root element (registry selector) into its
   group container, in member order. Web Components (Shadow DOM) carry their internals
   with them; non-component blocks (`.tacho-block`, `.gaps-block`, logos, maps) are
   self-styled and safe to move. Hidden modules (`show*` false) get `.section-hidden`
   exactly as today.
3. **Position** — for each group:
   - locked: pin to anchor with `--edge-z` inset (e.g. `top-right` → `top: var(--edge-z); right: var(--edge-z)`).
   - free: `left = (x · vw) / zoom`, `top = (y · vh) / zoom`, plus
     `transform: translate(-px·100%, -py·100%)` where `(px,py)` is the anchor's unit
     pivot (e.g. top-right → `translate(-100%, 0)`). Translate-% resolves against the
     zoomed border box, so no measurement is needed and zoom stays correct. The `/zoom`
     division mirrors today's `--edge-z = edge / scale` compensation (zoomed elements
     interpret their own `left/top` in zoomed units).
4. **Zoom** — `style.zoom` applies per group container (replacing today's per-element
   application in `settings.js` `applyZoom`).
5. **Measure & report** — §1.3 metrics after apply.

Corner-radius simplification: the direction-dependent "unified grid rounding"
(`dashboard.css` 39–54) dies. The engine stamps `data-edge="first|last"` on flow
members; CSS rounds outer corners per flow direction with two generic rules.

**Deleted at cutover (Phase 3):**

- `settings.js` `applyLayout()` (corner classes, commentary diagonal, secondary-edge
  derivation, incidents inline-style forcing, `bottomYOffset` margin patches) and the
  disabled vertical-swap remnants.
- `dashboard.css` `.layout-*` rules + direction-dependent radius rules.
- Legacy keys `layoutPosition`, `bottomYOffset` (after migration), and the dead C#
  `GroupPositions` stub (the overlay never read it).

**Kept:** click-through/idle-mode window behavior, `fs.watchFile` → `settings-sync`
pipeline, per-component rendering, performance mode, Drive HUD, race-control/pit-limiter/
race-end system overlays.

### Default groups (seed = today's top-right behavior)

| id | members | anchor (locked) |
|---|---|---|
| `mainHud` | tacho, logos (k10+car), controls, pedals, maps, position, gaps | top-right |
| `timer` | race timer | below mainHud (free, seeded from measured pos) |
| `secondary` | leaderboard, datastream, pitbox | bottom-right |
| `incidents` | incidents | bottom-left |
| `commentary` | commentary | bottom-left |
| `spotter` | spotter | bottom-center |
| `gameLogo` | game logo | bottom-left |

(Seeding for non-default `layoutPosition` values ports the old derivation table once,
in the host's migration step — §4.)

---

## 3. Host editor (prodrive-windows, Settings → Visual rebuild)

Per the locked decision, the editor **rebuilds the Visual tab in place** — the Visual
category switches from the two-column `FlushColumns` card stack to a full-width layout
mode (`SettingsPage.xaml.cs` is already stubbed for this: "PR-B replaces with drag/drop
editor", line 181).

### 3.1 Canvas

- Aspect-correct scaled viewport of the target display's work area.
- **Background: live screen preview.** Reuse the `AmbientRegionPicker` GDI BitBlt
  capture path (proven against the CsWinRT `IBufferByteAccess` pitfall — encode via
  `BitmapEncoder`, downscaled to canvas resolution with `BitmapTransform`) on a
  ~2–5 fps refresh timer, plus a dim scrim so proxies read clearly. Toggle to pause or
  switch to a neutral grid. (Upgrade path if smoother capture is ever wanted:
  `Windows.Graphics.Capture`; not needed for v1.)
- **Proxy layer:** a `Canvas` using the `AmbientRegionPicker` pointer pattern
  (PointerPressed/Moved/Released + capture). Each group renders as a bordered box with
  a name chip, containing member mini-boxes labeled with module names — **pixel-exact**
  from the metrics sidecar scaled to canvas space; registry `nominal` sizes until first
  metrics arrive.
- **Adorners:** 9 anchor dots, snap-zone highlights during drag, alignment guides
  (Phase 4).

### 3.2 Interactions

| Gesture | Result |
|---|---|
| Drag group box | Move group. Release inside an anchor's snap radius → `locked: true` at that anchor. Release elsewhere → free-hand: `locked: false`, anchor auto-set to nearest 9-point (sensible pivot), `x,y` normalized. |
| Drag module box out of its group | Break apart: module becomes a new single-member group at the drop point. |
| Drag module box onto another group | Join: appended to that group's members (insertion index from drop position along the flow axis). |
| Ctrl+click multi-select → "Group" | Merge selection into one group at the primary selection's position. |
| Eye icon on proxy / right-rail checkbox | Toggle the module's `show*` key. Hidden modules stay listed in the rail for re-adding. |
| Double-click group chip | Rename (`label`). |
| Right rail per group | 9-dot anchor picker, flow (row/column), gap, lock toggle. |
| Ctrl+Z / Ctrl+Y | Session-local undo/redo (snapshot stack of the layout block). |

### 3.3 Write path

- Mutations commit on **gesture end** (drag-release, toggle, property change) —
  debounced `OverlaySettingsService.SaveAsync`, never per-pointer-frame (the overlay's
  500 ms `fs.watchFile` poll makes per-frame writes pointless churn).
- If the overlay is running, it re-lays out live via `settings-sync` — the user sees
  the real HUD follow their proxy edits. Move-by-proxy holds: the host only ever writes
  JSON.
- Metrics watcher (`overlay-metrics.json`) refreshes proxy dimensions live.
- **Dual-write during transition:** the host also maintains best-effort legacy keys
  (`layoutPosition` from `mainHud`'s anchor, `bottomYOffset` = 0) so an older overlay
  binary still renders sanely in dev/skew scenarios. Dropped at cutover.

---

## 4. Migration & compatibility

- **Gate:** overlay checks `layout.version === 2` → v2 engine; otherwise legacy path.
  Both paths coexist through Phase 2.
- **Synthesis:** on first host run with no `layout` block, the host synthesizes groups
  from existing `layoutPosition` / `bottomYOffset` / `show*` by porting the old
  `applyLayout` derivation table (main corner → secondary opposite-vertical →
  incidents opposite-horizontal → commentary diagonal). The user's first preview
  matches what they already see.
- **Cutover (Phase 3):** host and overlay ship together in the combined Inno installer,
  so production never skews — legacy engine + keys are deleted one release after v2
  ships. Inventory cleanup rides along: drop dead `showFuel`/`showTyres` host toggles
  (the overlay's visibility map never had them — fuel/tyres live inside pitbox tabs
  now); add missing `showMaps` / `showTimer` keys (today always-visible, unlisted).

## 5. Testing

- **Phase 0 fixture:** one canonical settings JSON (layout block + registry) checked
  into both repos; C# round-trip test (deserialize → serialize → key-stable) and JS
  schema validation against the same bytes. Registry-drift test: host's embedded copy
  vs overlay's `layout-registry.json`.
- **Overlay (Playwright, already configured):** load fixture → assert each group
  container's `getBoundingClientRect` matches anchor math within tolerance (locked +
  free, multiple zooms); membership reparenting; hidden-module flow-skip; metrics file
  contents.
- **Host:** anchor/pivot math + migration synthesis + registry fallback as plain unit
  tests; editor gesture logic kept in a testable view-model (not page code-behind) per
  the [[winui-startup-testing-gap]] lesson — CI never launches the host, so logic must
  be testable headless.

## 6. Phases

| Phase | Repo(s) | Work |
|---|---|---|
| **0 — Contract** | both | Schema (`layout` block, metrics sidecar), `layout-registry.json` + extraResources packaging + host runtime read + embedded fallback, shared fixture + round-trip tests, inventory reconciliation (dead/missing `show*` keys). |
| **1 — Overlay engine** | overlay | `layout-v2.js` (containers, reparenting, anchor math, zoom), version gate, metrics IPC + sidecar writer, Playwright coverage. Legacy path untouched. |
| **2 — Host editor** | windows | Visual tab rebuild: live-capture canvas, proxy layer, full gesture set, right rail, undo, debounced writes, metrics watcher, dual-write legacy keys. |
| **3 — Migration & cutover** | both | First-run synthesis from legacy keys; delete legacy engine/CSS/keys + `GroupPositions` stub; ship combined installer. |
| **4 — Polish** | both | Multi-monitor (display picker + `layout.display`), snap/alignment guides, layout presets (save/load), possibly placeable system overlays. |

## Decision log

- **2026-06-10 (locked):** membership-only groups (no inter-group docking) · editor
  rebuilds Settings → Visual in place · proxies pixel-exact via a metrics channel,
  registry nominals as fallback.
- **2026-06-10 (this doc):** editor canvas = *live* screen capture with named proxy
  boxes, drag-to-move + drag-to-(un)group · metrics channel refined to a one-writer
  sidecar file (`overlay-metrics.json`) to avoid two-writer clobbering of
  `overlay-settings.json` · registry distributed as electron-builder `extraResources`
  read by the host at runtime, embedded copy as dev fallback · visibility remains in
  flat `show*` keys rather than moving into the layout block.
- **2026-06-10 (Phase 0 implementation):** member slots are `string | string[]` —
  one nesting level of perpendicular stacking, required to reproduce today's
  controls/pedals, position/gaps and logo pairs inside the row-flow main HUD ·
  the registry carries `defaultLayout` (the canonical seed groups) instead of a
  per-module `defaultGroup` field — group membership has one source of truth ·
  `OverlaySettings` gained `[JsonExtensionData]` so keys the host doesn't model
  (overlay-side `performanceMode`, `dsShow*`, future additions) survive the host's
  whole-object save instead of being silently stripped · `groupPositions` stub
  removed immediately (nothing ever read it) rather than waiting for Phase 3.
