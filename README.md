# RaceCor Pro Drive — Windows

Native Windows desktop app for [RaceCor Pro Drive](https://prodrive.racecor.io). WinUI 3 + C# .NET 8.

The CI release pipeline also bundles the SimHub plugin and the Electron HUD overlay (built from sibling repos) into a single installer — see [`installer/`](installer/).

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
└── CLAUDE.md
```

Cross-repo agent docs live under [`agents/`](agents/) (git submodule).

## v0 features

- OAuth 2.0 PKCE sign-in with Discord
- Secure token storage (Windows Credential Manager / `PasswordVault`)
- Dashboard with stat tiles
- Paginated race sessions list with filtering
- Session detail view with lap telemetry
- Dark theme (v1 will add accent-color integration)

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
- **Services**: `AuthService`, `ApiClient`, `TokenStore` (shared across pages)

### Key services

- **AuthService** — orchestrates OAuth 2.0 PKCE flow (codeverifier/challenge, browser auth, callback via protocol handler, code → token exchange, auto-refresh on 401)
- **TokenStore** — wraps Windows `PasswordVault` for OS-level encrypted token persistence
- **ApiClient** — HTTP wrapper that auto-attaches `Authorization: Bearer`, handles JSON, retries on 401 with refresh

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
- `GET /api/v1/dashboard` — dashboard stats (stub)

## Pages

| Page | Purpose |
|------|---------|
| **LoginPage** | Discord sign-in button |
| **DashboardPage** | Stat tiles, recent sessions |
| **SessionsPage** | Filterable, paginated race list |
| **SessionDetailPage** | Session header, lap telemetry |
| **PlaceholderPage** | Reusable stub for Moments / Tracks / Cars / DNA / When / Safety / Composure / Debrief / Settings |

## Building for release

The combined installer (host + Electron HUD + SimHub plugin) is built by the [`installer/`](installer/) tooling. CI fires on a `v*` tag push — see [`.github/workflows/release.yml`](.github/workflows/release.yml).

Local installer build:

```powershell
pwsh -File installer/build.ps1
```

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

## Troubleshooting

### "Cannot find type name" errors
Ensure the **Windows App SDK** NuGet is installed: right-click solution → Manage NuGet Packages → search "Windows App SDK".

### 401 Unauthorized on API calls
Check `TokenStore.LoadTokens()` → `expiresAt`. `AuthService` should auto-refresh; check network logs if not.

### Protocol handler not firing
Verify `HKEY_CURRENT_USER\Software\Classes\racecor-prodrive` in `regedit`. The `(Default)` value must point to your actual `.exe`. Restart Explorer or reboot if changes don't take.

### XAML binding failures
Confirm `DesignSystem.xaml` is merged in `App.xaml.Resources` and that element names match `x:Name` in code-behind.

## Known limitations (v0)

- Unpackaged — no Store integration yet
- `/api/v1/dashboard` is a stub
- 9 nav items are `PlaceholderPage` (Moments, DNA, When, etc.)
- Dark theme only — v1 will add light mode + accent-color integration
- No offline mode

## Next steps

See [`next-steps.md`](next-steps.md) for the v1+ roadmap.

## License

Copyright (c) 2026 K10 Motorsports / Kevin Conboy. All rights reserved. See [`LICENSE`](LICENSE).

## Contact

- **Developer**: Kevin Conboy (kev@alternate.org)
- **Org**: K10 Motorsports
