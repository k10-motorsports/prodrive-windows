# Diagnostics pipeline

Lets a developer on another machine (typically Claude running on the Mac)
stream structured logs and pull on-demand snapshots from a running
RaceCor Pro Drive on the Windows PC — no GitHub roundtrip required.

## Architecture

```
Windows PC                                           Mac (Claude)
─────────────────────────────────────────            ─────────────────────────
RaceCorProDrive.exe                                  agents/prodrive-windows/scripts/diag/watch.sh
  │                                                    │
  ├─ DiagLog.Start()  ──── writes ──▶  diag-*.jsonl    │
  │                       (ring of 5000 in-memory)     │  curl /diag/tail?since=N
  │                                                    │  every 500ms
  └─ DiagServer.Start() ─ HTTP on 0.0.0.0:8891 ◀───────┘
        /diag/info        (identity + URLs)
        /diag/tail        (new ring entries by seq)
        /diag/logs        (list files)
        /diag/logs/<n>    (raw file)
        /diag/snapshot    (zip of every log + system-info.json)
```

Two storage tiers, one record shape:
- **In-memory ring** (last 5000 entries) — what `/diag/tail` reads. Cheap
  for high-frequency polling.
- **JSONL file** — `%LOCALAPPDATA%\RaceCorProDrive\Logs\diag-<utc>-pid<n>.jsonl`,
  one per process run, last 10 retained. Source of truth for replay.

## Record format

Every log line — on disk or over HTTP — uses the same compact shape:

```json
{
  "seq": 42,
  "ts": "2026-05-16T17:42:01.1234567Z",
  "lvl": "info",
  "cat": "overlay.render",
  "msg": "device lost",
  "props": { "adapter": "NVIDIA RTX 4090", "removed": true },
  "exc": {
    "type": "System.InvalidOperationException",
    "message": "...",
    "stack": "   at ..."
  }
}
```

`props` and `exc` are omitted when empty.

## Categories

Use dotted names so the watcher can filter by prefix:

- `app.boot` — Program.cs Main path
- `app.lifecycle` — App.xaml.cs bootstrap
- `crash.*` — unhandled exceptions (XamlUnhandled, DomainUnhandled, UnobservedTask)
- `overlay.render`, `overlay.hook`, `overlay.deviceLost` — DXGI/D3D + injection
- `iracing.sdk`, `iracing.detector` — sim integration
- `auth`, `recording`, `update` — feature areas
- `diag.server` — the diag endpoint itself (request count, bind failures)

Add new categories as needed; the consumer side doesn't need to know
about them in advance.

## Opting in / out

Defaults: **enabled, port 8891, bound to 0.0.0.0** (any interface).
There is no authentication — anyone on the network can read the log
stream and pull a snapshot.

Override via `%LOCALAPPDATA%\RaceCorProDrive\settings.json`:

```json
{
  "racecor.diag.enabled": "true",
  "racecor.diag.port": "8891",
  "racecor.diag.bindAny": "true"
}
```

Set `bindAny` to `false` to bind to loopback only (e.g. when reaching
in via SSH port-forward instead of LAN). Set `enabled` to `false` to
disable the server entirely without removing the code.

## Usage from the Mac

All scripts live in `agents/prodrive-windows/scripts/diag/` and accept `<host>` or `<host>:port`:

```bash
# 1. Confirm the PC is reachable + see the bound URLs
agents/prodrive-windows/scripts/diag/info.sh 192.168.1.42

# 2. Stream logs live — appends to ~/.racecor-diag/live.jsonl
agents/prodrive-windows/scripts/diag/watch.sh 192.168.1.42

# 3. Pull a full bundle (logs + system-info) on demand
agents/prodrive-windows/scripts/diag/snapshot.sh 192.168.1.42
# → extracted to ~/.racecor-diag/snapshots/racecor-diag-<utc>/
```

`watch.sh` is the main loop. It discovers the current `head` seq on
start, polls every 500 ms, detects server restarts (head goes
backwards) and resets the cursor, and pretty-prints to stdout while
preserving the raw JSONL on disk.

When Claude is reading the stream, it can `tail -F ~/.racecor-diag/live.jsonl`
or grep the JSONL directly for a specific category/seq/timestamp range.

## Adding instrumentation

From any code under `RaceCorProDrive`:

```csharp
using RaceCorProDrive.Diagnostics;

DiagLog.Info("overlay.render", "frame committed",
    new Dictionary<string, object?> { ["frameNs"] = 16_700_000 });

DiagLog.Warn("iracing.sdk", "telemetry header missing");

try { /* ... */ }
catch (Exception ex)
{
    DiagLog.Exception("overlay.hook", ex, "DXGI present hook failed");
}

DiagLog.Event("overlay", "deviceLost",
    new Dictionary<string, object?> { ["adapter"] = adapter.Name });
```

The logger never blocks the caller (queue is bounded; overflow drops
the *new* record rather than back-pressuring) and never throws.

## Wire-up locations

- `Program.cs:Main` — `DiagLog.Start()` runs before any WinUI bootstrap
  so even pre-init failures land in the stream.
- `App.xaml.cs:OnLaunched` — `DiagServer.Shared.Start()` next to
  `RecordingControlServer.Shared.Start()`.
- `App.xaml.cs:LogCrash` and `BootTrace` — mirror their existing
  `crash.log`/`boot.log` writes into `DiagLog` so a remote watcher
  sees the same events without parsing two formats.

## Why a separate server (not extend RecordingControlServer)

`RecordingControlServer` is intentionally loopback-only (it rejects
non-loopback callers explicitly at the top of every request) because
it accepts POSTs that mutate state. The diag server is GET-only and
binds the LAN interface, so the security postures don't match —
keeping them as two listeners on two ports is cleaner than gating
each route differently.

## Future extensions

These are deliberately out of scope for v1 but cheap to add later:

- **SSE streaming** at `/diag/tail/stream` — eliminates the 500 ms
  poll lag. Mac side becomes `curl -N` instead of a loop.
- **`Microsoft.Extensions.Logging` provider** that funnels `ILogger<T>`
  output into `DiagLog`, so call sites don't need to know about us.
- **Screenshot on snapshot** — via the existing recording pipeline's
  GraphicsCapture path; would let Claude see overlay alignment issues
  visually, not just textually.
- **Authenticated mode** — currently the endpoint is open. Adding a
  shared-secret query param (or a Bearer header) is one helper method.
