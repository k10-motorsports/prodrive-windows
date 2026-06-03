# Recording pipeline migration — Electron overlay → WinUI host

**Status:** Phase 1 + Phase 2 wire-up shipped 2026-05-11. Default backend is still `electron`; native is opt-in via Settings → Recording → Backend. Tested via dotnet stub-build only — needs validation on a real Windows machine.
**Date:** 2026-05-11

## Why we're moving recording out of the overlay

The Electron overlay window is configured as a transparent, click-through, focus-less, always-on-top, screen-saver-level surface that re-asserts z-order every five seconds. That set of properties is correct for an in-game HUD and fundamentally wrong for hosting hardware capture:

1. **Permissions can't be granted.** Chromium prompts for `media` (camera/mic) need a focusable, visible UI. With `focusable: false` + `setIgnoreMouseEvents(true)`, there is no surface for the user to click "Allow". Result: `getUserMedia({video: ...})` silently fails, the camera never opens, the green light never turns on. Phase 1 papered over this with `setPermissionRequestHandler` granting `media` outright — fine for a trusted local origin, but it's a band-aid.
2. **Device IDs don't translate across the runtime boundary.** The WinUI host enumerates `Windows.Devices.Enumeration.DeviceClass.VideoCapture` and persists a WinRT `DeviceInformation.Id` (`\\?\USB#VID_…#{guid}`) — but Chromium `getUserMedia({deviceId: {exact: …}})` expects its own origin-scoped SHA-256 hash. The two namespaces never overlap, so the saved id always misses. Phase 1 added a companion friendly-label field as a translation bridge.
3. **The overlay carries far more than it should.** The renderer currently owns display capture, canvas-based facecam PiP compositing, audio mixing via Web Audio API, MediaRecorder lifecycle, chunked IPC writes to disk, FFmpeg transcoding, `.rcpdv` bundling, telemetry sidecar I/O, replay buffer, and auto-record state. All of that competes with the HUD render loop on the same renderer thread, gives Chromium a much larger attack surface for "why isn't my camera working," and duplicates work the WinUI host could do natively.
4. **Cross-process data flow is backwards.** Today: SimHub plugin → Electron overlay (HTTP poll) → web app window → WinUI host (via overlay-settings.json polling). The right shape is: SimHub plugin → WinUI host → overlay & web app as views. Recording capture should run alongside the WinUI host where the device IDs and hardware permissions already belong.

OBS captures the same hardware reliably during the same sessions specifically because it bypasses every one of those Electron-layer mediators and talks directly to DirectShow + Media Foundation. The target architecture below does the same thing — uses FFmpeg shelled from C# with native DirectShow / gdigrab filters.

## Phase 1 — shipped 2026-05-11

Tactical fix so the facecam stops being completely broken while the migration is in flight.

| Change | File |
|---|---|
| Grant `media` + `display-capture` permission for the overlay's local origin | `prodrive-overlay/main.js` (`setPermissionRequestHandler`) |
| Persist friendly device label alongside the WinRT id | `prodrive-windows/src/.../OverlaySettings.cs` (`Recording{Mic,SystemAudio,Webcam}Label`) |
| Wire the SettingsPage device pickers to also write the label, with backfill for legacy saves | `DevicePickerRow.cs`, `SettingsPage.xaml.cs` |
| Resolve persisted label → live Chromium deviceId at recording start, fall back to default rather than abort | `prodrive-overlay/modules/js/recorder.js` (`resolveDeviceId`) |
| Delete the dead `settingsRec*` DOM population code that targeted elements no longer in `dashboard.html` | `prodrive-overlay/modules/js/recorder-ui.js` |

After Phase 1, the camera should bind successfully and the green light should come on. The recording continues to live in Electron — Phase 1 doesn't move anything, it just stops it from being broken.

## Phase 2 — native recording in the WinUI host

Target architecture:

```
WinUI host (RaceCorProDrive.exe)
│
├── Recording/NativeRecordingService.cs
│     │
│     ├── reads OverlaySettings (devices, quality, output format)
│     ├── reads LibraryService (output directory, filename slugging)
│     ├── spawns ONE ffmpeg.exe child process per recording session
│     │       │
│     │       └── gdigrab/ddagrab (screen)
│     │           + dshow video=<webcam label>   (facecam, PiP via filter_complex overlay)
│     │           + dshow audio=<mic label>      (microphone)
│     │           + dshow audio=<vcable label>   (system loopback via virtual cable)
│     │           → h264_nvenc / h264_qsv / libx264 → .mp4
│     │
│     └── exposes Start(opts) / Stop() / State
│
└── (later) HTTP/named-pipe endpoint that the overlay's existing
            Ctrl+Shift+Q hotkey can call into when NativeRecording
            is enabled. Until that endpoint exists, the overlay
            keeps its own recording pipeline as the live path.
```

The big simplifications versus today's overlay pipeline:

- **One FFmpeg invocation** does capture + composite + encode. No `MediaRecorder` → IPC chunks → write stream → post-process transcode dance.
- **No canvas-based PiP draw loop** competing with the HUD on the renderer's main thread. The PiP overlay is just `-filter_complex "[1:v]scale=W:H[cam];[0:v][cam]overlay=…"`.
- **No permission plumbing.** DirectShow + gdigrab are owned by the C# process at OS level, no Chromium policy involved.
- **Same friendly label that Phase 1 introduced** flows into the dshow filter args (e.g. `-f dshow -i video="Logitech BRIO"`) — no further translation needed in the common case.

### Rollout plan

The Electron pipeline keeps working in parallel. The `recordingBackend` setting governs which side captures:

| Value | Behavior |
|---|---|
| `"electron"` (default) | Existing renderer-driven recording. Phase 1 fixes apply. |
| `"native"` (opt-in beta) | WinUI host's `NativeRecordingService` owns the capture. The overlay's existing hotkey + auto-record paths still trigger `recorder.startRecording()` — the renderer reads the flag and routes through `window.k10.startNativeRecording` (IPC) → main.js HTTP client → host's `RecordingControlServer`. Overlay's local capture is skipped. |

When `"native"` is proven stable, flip the default and start removing the Electron-side modules (`recorder.js`'s capture code, `ffmpeg-encoder.js`, `bundle-writer.js`, `auto-record.js`, `replay-buffer.js`, the `start-recording` / `write-recording-chunk` / `stop-recording` IPC handlers, the `ffmpeg-static` dep from `electron-builder.yml`).

### What Phase 2 wired up (2026-05-11)

| Piece | File |
|---|---|
| `RecordingService.StartAsync/StopAsync` with full FFmpeg argv builder (gdigrab + dshow + filter_complex PiP + amix + encoder selection) | `Recording/NativeRecordingService.cs` |
| Loopback HTTP control surface (`POST /v1/recording/{start,stop}`, `GET /v1/recording/state`, `GET /v1/health`) bound to 127.0.0.1, port 8890 (falls back to OS-assigned). Loopback-only enforcement at the request level. | `Recording/RecordingControlServer.cs` |
| Bound port persisted to `overlay-settings.json` as `hostControlPort` for overlay discovery | same file |
| Server start in app boot | `App.xaml.cs` (`OnLaunched`) |
| `recordingBackend` + `hostControlPort` schema fields + electron default | `Services/OverlaySettings.cs` |
| Settings → Recording → Backend segmented toggle (Electron / Native (beta)) | `Pages/SettingsPage.xaml.cs` |
| FFmpeg locator extended to find the overlay's `ffmpeg-static` binary so users don't need a separate install | `NativeRecordingService.cs` (`FFmpegLocator`) |
| Overlay preload bridge: `startNativeRecording` / `stopNativeRecording` / `getNativeRecordingState` | `prodrive-overlay/preload.js` |
| Overlay main-process HTTP client that proxies preload calls to the host | `prodrive-overlay/main.js` |
| Overlay renderer: `recorder.startRecording()` checks `recordingBackend` and routes to native when set; `stopRecording()` mirrors; UI indicator + timer share state with the Electron path so the visual experience is unchanged | `prodrive-overlay/modules/js/recorder.js` |

### Open items for Phase 2

- [ ] **Validate on a real Windows machine.** Everything compiles in isolation but the FFmpeg + dshow args have only been reasoned about, not run. First test: flip the toggle, hit Ctrl+Shift+Q, confirm a working MP4 lands in `Videos\`.
- [ ] Encoder probe at install/first-run time. The arg builder currently maps `"auto"` → `h264_nvenc` blindly; mirror `prodrive-overlay/modules/js/ffmpeg-encoder.js`'s detection (parse `ffmpeg -encoders` for `h264_nvenc` / `h264_qsv` / `h264_amf` / `libx264`) and pick the best available.
- [ ] Bundle `ffmpeg.exe` with the WinUI installer (`installer/RaceCorProDrive.iss`) so the host doesn't depend on the overlay's ffmpeg-static being present. Until then, `FFmpegLocator` falls back to the overlay's bundled copy or `PATH`.
- [ ] Decide whether `.rcpdv` bundling (telemetry-sidecar fusion) stays in the host or moves to the post-processing service. Lean toward host — `BundleReader.cs` is already there.
- [ ] Replay buffer (always-on rolling capture for save-after-the-fact clips): FFmpeg supports this natively via segmented output + `-f segment -segment_time 30 -segment_wrap 2` or via the `-stream_loop` + memory ringbuffer pattern. Spike both before committing.
- [ ] Auto-record state machine (start on session begin, stop on pit / session end): currently in `prodrive-overlay/modules/js/auto-record.js`, reading `poll-engine` events. The poll-engine itself should also move host-side eventually; until then, the host can subscribe to the same SimHub WebSocket the overlay does.
- [ ] Telemetry sidecar: today the overlay writes `.telemetry.jsonl` synchronized to its own recording chunks. For the native path, the host needs to either receive the telemetry stream (e.g. via a `POST /v1/recording/telemetry` push from the overlay) or read it directly from the SimHub plugin. Latter is cleaner — moves the data flow toward `plugin → host` per your bigger-picture point.

### Related migrations to follow

The same "this lives in the overlay but shouldn't" pattern applies to:

- **Auto-updater** — `prodrive-overlay/modules/js/auto-updater.js` overlaps with `prodrive-windows/.../UpdateService.cs`. Pick one (the host) and drop the overlay's copy.
- **K10 / Discord auth IPC** — overlay has `k10-connect`, `discord-connect` handlers; the host already has `Auth/AuthService.cs`. The overlay should consume tokens from the host, not initiate auth itself.
- **LAN remote server** (`prodrive-overlay/remote-server.js`) — proxies SimHub for iPad clients. Better as a Kestrel service in the host.
- **Stream Deck plugin auto-install** — currently in `prodrive-overlay/main.js`; could move to host's first-run setup.
