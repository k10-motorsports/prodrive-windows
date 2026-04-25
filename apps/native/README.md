# RaceCor Pro Drive — Windows Native App

Fully native Windows desktop application for broadcast-grade sim racing analytics. Built with WinUI 3 + C# .NET 8.

## Overview

This is one of 7 parallel native applications in the Pro Drive ecosystem. Unlike Electron or Chromium-based approaches, this app uses the modern Windows App SDK for native performance and OS integration.

**v0 features:**
- OAuth 2.0 PKCE sign-in with Discord
- Secure token storage (Windows Credential Manager)
- Dashboard with stat tiles
- Paginated race sessions list with filtering
- Session detail view with lap telemetry
- Dark theme (v1 will add accent-color integration)

## Requirements

- **Windows 10 (build 2004)** or later (Windows 10.0.19041.0)
- **Visual Studio 2022 17.8** or later
- **.NET 8 SDK**
- **Windows App SDK** workload (auto-installed with Visual Studio)

## Getting Started

### 1. Open the Solution

```bash
git clone https://github.com/alternatekev/racecor-prodrive-win.git
cd racecor-prodrive-win
start RaceCorProDrive.sln
```

Or open directly in Visual Studio.

### 2. Build

```bash
# Via Visual Studio
# File → Open → RaceCorProDrive.sln → Build (Ctrl+Shift+B)

# Or via CLI
dotnet build
```

### 3. Debug

```bash
# Press F5 in Visual Studio
# Or
dotnet run
```

The app will launch in unpackaged mode (simpler for development).

### 4. Protocol Handler Registration (OAuth callback)

OAuth sign-in requires the `racecor-prodrive://` URI scheme to be registered:

```bash
# On Windows (requires admin):
regedit /s assets/register-scheme.reg

# Edit the .reg file first:
# - Replace <USERNAME> with your actual Windows username
# - Update path to point to your build output (e.g., bin/Debug/net8.0-windows10.0.19041.0/RaceCorProDrive.exe)
```

**Verify it works:**
```bash
explorer racecor-prodrive://auth?code=test&state=test
# Should launch the app
```

## Architecture

### MVVM-Lite Pattern

- **Pages** (XAML + code-behind): Handle UI layout and user events
- **ViewModels**: Simple classes managing page state (ObservableCollection, etc.)
- **Services**: AuthService, ApiClient, TokenStore (shared across pages)

### Key Services

- **AuthService**: Orchestrates OAuth 2.0 PKCE flow
  - Generates code verifier + challenge
  - Opens browser to authorization endpoint
  - Handles callback via protocol handler
  - Exchanges code for tokens
  - Auto-refreshes on 401

- **TokenStore**: Secure token persistence
  - Wraps Windows PasswordVault (OS credential store)
  - Encrypts tokens with user's Windows login
  - First-run friendly

- **ApiClient**: HTTP client wrapper
  - Auto-attaches `Authorization: Bearer` header
  - Handles JSON serialization
  - Retries on 401 with token refresh

### Design System

Colors and typography defined in `DesignSystem/Tokens.cs`:
- **K10 Red** #E53935 (primary accent)
- **Background** #0A0A14 (dark)
- **Text** #FFFFFF, #A0A0A0, #707070 (hierarchy)
- **Fonts** Segoe UI Variable (display + body), Cascadia Code (mono) 2014 system fonts only

## API Integration

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
| **PlaceholderPage** | Reusable stub for Moments/Tracks/Cars/DNA/When/Safety/Composure/Debrief/Settings |

## Building for Release (MSIX)

### Unpackaged → MSIX Transition

1. **Update csproj:**
   ```xml
   <WindowsPackageType>MSIX</WindowsPackageType>
   ```

2. **Obtain signing certificate:**
   - Self-signed for dev/sideload
   - EV cert for Windows Store submission
   - See `next-steps.md` for procurement details

3. **Right-click project → Publish → Create App Packages**
   - Select "For Self-Hosting"
   - Follow wizard to sign

4. **Output:** `AppPackages/RaceCorProDrive_0.1.0.0_x64.msix`

5. **Install locally:**
   ```powershell
   Add-AppxPackage -Path .\AppPackages\RaceCorProDrive_0.1.0.0_x64.msix
   ```

6. **Submit to Store:**
   - Create Partner Center account (K10 Motorsports org)
   - Create app entry
   - Upload signed .msix
   - Fill store listing (screenshots, description, etc.)

## Troubleshooting

### "Cannot find type name" errors
- Ensure `WindowsAppSDK` NuGet is installed: right-click solution → Manage NuGet Packages → search "Windows App SDK"

### 401 "Unauthorized" on API calls
- Check if token is expired: `TokenStore.LoadTokens()` → `expiresAt`
- AuthService should auto-refresh, but check network logs if it's not working

### Protocol handler not firing
- Verify Registry: `regedit` → `HKEY_CURRENT_USER\Software\Classes\racecor-prodrive`
- Path in `(Default)` must point to your actual .exe
- Restart Windows Explorer or reboot if changes don't take effect

### XAML binding failures
- Ensure `DesignSystem.xaml` is merged in `App.xaml.Resources`
- Check element names match `x:Name` in code-behind

## File Structure

```
RaceCorProDrive/
├── src/RaceCorProDrive/
│   ├── Pages/              # XAML pages
│   ├── Auth/               # AuthService, PkceHelper, TokenStore
│   ├── Api/                # ApiClient, Models
│   ├── DesignSystem/       # Tokens, theme resources
│   ├── Support/            # AppLogger
│   ├── RaceCorProDrive.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── Package.appxmanifest
│   └── app.manifest
├── assets/                 # Icon, registry script
├── .gitignore
├── RaceCorProDrive.sln
├── README.md               # This file
├── CLAUDE.md               # Architecture & development guide
├── next-steps.md           # v1+ roadmap
└── LICENSE
```

## Known Limitations

- **v0 is unpackaged**: no Store integration yet
- **Dashboard endpoint is stub**: `/api/v1/dashboard` returns placeholder data
- **9 nav items are PlaceholderPage**: Moments, DNA, When, etc. need full implementations
- **Dark theme only**: v1 will add light mode + accent-color integration
- **No offline mode**: all pages require internet

## Next Steps

See `next-steps.md` for detailed v1+ roadmap:
- Dashboard enrichment (real stats)
- Moments, DNA, When page implementations
- MSIX packaging + Store submission
- EV certificate integration
- Notifications, jump lists, taskbar badges
- Accessibility pass
- ARM64 support

## Development Tips

### Hot Reload
- While debugging, edit XAML and save
- Visual Studio auto-recompiles; see changes on next F5
- C# code changes require rebuild

### Logging
- Output goes to Visual Studio Debug output window
- AppLogger wraps `Microsoft.Extensions.Logging.Debug`
- Add more loggers in `Support/AppLogger.cs`

### Testing Protocol Handler
```bash
# Simulate OAuth callback
explorer "racecor-prodrive://auth?code=AUTH_CODE&state=STATE"
```

## Contributing

See `CLAUDE.md` for architecture decisions and codebase conventions.

## License

Copyright (c) 2026 K10 Motorsports / Kevin Conboy. All rights reserved.

See `LICENSE` file for details.

## Contact

- **Developer**: Kevin Conboy (kev@alternate.org)
- **Organization**: K10 Motorsports
- **Repository**: https://github.com/alternatekev/racecor-prodrive-win (private)
