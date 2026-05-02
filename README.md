# RaceCor Pro Drive — Windows

Native Windows desktop app for [RaceCor Pro Drive](https://prodrive.racecor.io). WinUI 3 + C# .NET 8.

The CI release pipeline produces a single Inno Setup installer that bundles this desktop app **plus the Electron HUD overlay** (built from the [`prodrive-overlay`](https://github.com/k10-motorsports/prodrive-overlay) sibling repo). The installer does **not** bundle the SimHub plugin — that's a separate install from [`prodrive-plugin`](https://github.com/k10-motorsports/prodrive-plugin). See [`installer/`](installer/).

## Repo layout

```
prodrive-windows/
├── src/RaceCorProDrive/    WinUI 3 app (C#, .NET 8)
├── assets/                 Icon, protocol-handler .reg
├── installer/              Inno Setup script + build orchestration
├── .github/workflows/      Release pipeline (tag-triggered)
├── RaceCorProDrive.sln
├── next-steps.md           v1+ roadmap
├── LICENSE
├── README.md               This file
└── CLAUDE.md               Pointer to canonical agents/ docs
```

Cross-repo agent docs live under [`agents/`](agents/) (git submodule).

## What the app does

A first-class Pro Drive desktop client — far beyond the original "shell + installer" framing. Nine pages, all behind Discord OAuth:

| Page | Purpose |
|------|---------|
| **Login** | OAuth 2.0 PKCE sign-in with Discord (browser handoff via custom protocol handler) |
| **Dashboard** | Stat tiles, HUD-status indicator, "launch overlay" FAB |
| **Sessions** | Paginated race list with category filtering, fetched from `prodrive.racecor.io/api/v1/sessions` |
| **SessionDetail** | Session header, lap telemetry, AI debrief from server |
| **Library** | Local `.rcpdv` race-bundle browser (MP4 + telemetry sidecar). Click in, scrub MP4 with telemetry overlay |
| **Editor** | MP4 viewer with bundle metadata overlay (clip prep — early stage) |
| **Settings** | 5 tabs: Visual, Commentary, Recording, **Hardware** (Moza wheel/pedal/shifter/handbrake), System |
| **PlaceholderPage** | Stubs for Moments, Tracks, Cars, DNA, When, Safety, Composure, Debrief |

Cross-cutting behaviors:

- **iRacing detector** (`Services/IRacingDetector.cs`) polls `Local\IRSDKMemMapFileName` every 5s; auto-launches the bundled overlay (`Overlay\RaceCorOverlay.exe`) when iRacing starts
- **Plugin status** — Dashboard polls `http://localhost:8889/racecor-io-pro-drive/` for the live HUD-status indicator
- **LAN sharing via mDNS/Bonjour** — advertises `prodrive-share._tcp` (Zeroconf NuGet) so other Pro Drive desktop apps on the same LAN can discover and pull race bundles
- **Shared overlay settings** — host writes `%APPDATA%\RaceCor.io\overlay-settings.json`; the bundled overlay reads it

## Requirements

- **Windows 10 (build 2004)** or later (10.0.19041.0)
- **Visual Studio 2022 17.8** or later
- **.NET 8 SDK**
- **Windows App SDK** workload (auto-installed with Visual Studio)

## Getting started

```bash
git clone --recurse-submodules https://github.com/k10-motorsports/prodrive-windows.git
cd prodrive-windows
start RaceCorProDrive.sln
```

Or via CLI:

```bash
dotnet build
dotnet run --project src/RaceCorProDrive
```

The app launches in unpackaged mode for development.

### Protocol handler registration (OAuth callback)

OAuth sign-in requires the `racecor-prodrive://` URI scheme to be registered:

```powershell
# Edit assets/register-scheme.reg first:
#   - Replace <USERNAME> with your Windows username
#   - Update path to your build output (bin/Debug/net8.0-windows10.0.19041.0/RaceCorProDrive.exe)
regedit /s assets/register-scheme.reg
```

Verify:

```bash
explorer racecor-prodrive://auth?code=test&state=test
# Should launch the app
```

## Architecture

### MVVM-Lite

- **Pages** (XAML + code-behind): UI layout and user events
- **ViewModels**: simple classes managing page state (`ObservableCollection`, etc.)
- **Services**: `AuthService`, `ApiClient`, `TokenStore`, `IRacingDetector`, `OverlayLauncher`, `BundleReader`, `LibraryService`, plus 12 services in `Services/Moza/` for hardware

### Key services

- **AuthService** — orchestrates OAuth 2.0 PKCE flow (codeverifier/challenge, browser auth, callback via protocol handler, code → token exchange, auto-refresh on 401)
- **TokenStore** — wraps Windows `PasswordVault` for OS-level encrypted token persistence
- **ApiClient** — HTTP wrapper that auto-attaches `Authorization: Bearer`, handles JSON, retries on 401 with refresh
- **IRacingDetector** — polls iRacing SDK shared-memory existence; raises events for OverlayLauncher
- **OverlayLauncher** — spawns `Overlay\RaceCorOverlay.exe` (path relative to host binary)
- **BundleReader / LibraryService** — read `.rcpdv` race bundles for the Library + Editor pages
- **Services/Moza/** — 12 services for Moza wheel / pedal / shifter / handbrake configuration via `System.IO.Ports` + `System.Management`
- **BonjourBrowser / LanSender** — Zeroconf-based LAN sharing of bundles

### Design system

`src/RaceCorProDrive/DesignSystem/Tokens.cs`:
- **K10 Red** `#E53935` (primary accent)
- **Background** `#0A0A14` (dark)
- **Text** `#FFFFFF`, `#A0A0A0`, `#707070` (hierarchy)
- **Fonts** Segoe UI Variable (display + body), Cascadia Code (mono) — system fonts only

## API

All requests to `https://prodrive.racecor.io/api/v1/*` require:

```
Authorization: Bearer <access_token>
```

Endpoints implemented:
- `POST /api/plugin-auth/token` — exchange code / refresh token
- `GET /api/v1/me` — current user profile
- `GET /api/v1/sessions?limit=50&offset=0&category=all` — paginated sessions
- `GET /api/v1/sessions/:id` — session detail + laps
- `GET /api/v1/dashboard?tz=...` — dashboard aggregates
- `GET /api/v1/tokens/native?format=cs` — design tokens for native rendering

## Building for release

The combined installer (host + Electron HUD overlay) is built by the [`installer/`](installer/) tooling. CI fires on a `v*` tag push — see [`.github/workflows/release.yml`](.github/workflows/release.yml).

Local installer build:

```powershell
pwsh -File installer/build.ps1
```

The installer's PowerShell script defaults `$OverlayRepo` to `../../prodrive-overlay/`. Override with the env var `RACECOR_OVERLAY_PATH` if your sibling checkout is elsewhere.

### Unpackaged → MSIX (Microsoft Store)

1. Update `src/RaceCorProDrive/RaceCorProDrive.csproj`:
   ```xml
   <WindowsPackageType>MSIX</WindowsPackageType>
   ```
2. Obtain a signing cert (self-signed for sideload; EV cert for Store submission). See `next-steps.md`.
3. Right-click project → Publish → Create App Packages → "For Self-Hosting".
4. Output: `AppPackages/RaceCorProDrive_<version>_x64.msix`.
5. Install locally:
   ```powershell
   Add-AppxPackage -Path .\AppPackages\RaceCorProDrive_0.1.0.0_x64.msix
   ```
6. Submit via Partner Center (K10 Motorsports org).

Bundle identifier: `racing.k10motorsports.prodrive.racecor.win`.

## Release wave

This repo is **Wave 2** of the six-repo lockstep release (alongside macOS / iOS / tvOS). Triggered after **Wave 1** (plugin + overlay) publishes their GitHub releases — the installer's build script downloads the latest plugin + overlay artifacts at release time. See the orchestrator at `agents/.claude/commands/release.md`.

## Troubleshooting

### "Cannot find type name" errors
Ensure the **Windows App SDK** NuGet is installed: right-click solution → Manage NuGet Packages → search "Windows App SDK".

### 401 Unauthorized on API calls
Check `TokenStore.LoadTokens()` → `expiresAt`. `AuthService` should auto-refresh; check network logs if not.

### Protocol handler not firing
Verify `HKEY_CURRENT_USER\Software\Classes\racecor-prodrive` in `regedit`. The `(Default)` value must point to your actual `.exe`. Restart Explorer or reboot if changes don't take.

### XAML binding failures
Confirm `DesignSystem.xaml` is merged in `App.xaml.Resources` and that element names match `x:Name` in code-behind.

### Overlay doesn't auto-launch with iRacing
- Check that `Overlay\RaceCorOverlay.exe` exists in the install directory. (If you ran from source, the overlay isn't there — install the packaged build to test the full launcher chain.)
- The detector polls every 5s, so iRacing may need to be running for a moment before the overlay spawns.
- Manual launch from Dashboard → "Launch overlay" works regardless.

## Known limitations (v0)

- Unpackaged — no Store integration yet
- Some `/api/v1/dashboard` aggregates are stubs server-side
- 9 nav items are `PlaceholderPage` (Moments, DNA, When, etc.)
- Dark theme only — v1 will add light mode + accent-color integration
- No offline mode
- LAN sharing requires both peers on the same broadcast domain (no VPN traversal)

## Next steps

See [`next-steps.md`](next-steps.md) for the v1+ roadmap.

## License

Copyright (c) 2026 K10 Motorsports / Kevin Conboy. All rights reserved. See [`LICENSE`](LICENSE).

## Contact

- **Developer**: Kevin Conboy (kev@alternate.org)
- **Org**: K10 Motorsports
