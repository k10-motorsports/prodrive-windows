# RaceCor Pro Drive — Windows Electron Shell

Electron wrapper that loads https://prodrive.racecor.io in a BrowserWindow. Provides a native .exe installer, MSIX (Microsoft Store) variant, custom URL scheme (`racecor-prodrive://`), and auto-updates via GitHub Releases.

## Quick Start

### Development

```bash
npm install
npm run dev
```

Opens the app in a BrowserWindow pointing to `https://prodrive.racecor.io` (or `$RACECOR_URL` env var).

### Building for Windows

```bash
npm run build:win
```

Produces:
- **NSIS Installer** (`dist/RaceCor Pro Drive 1.0.0.exe`) — clickable installer, Start-menu shortcut, desktop shortcut, uninstaller
- **MSIX** (`dist/RaceCor Pro Drive 1.0.0.msix`) — Microsoft Store submission variant

### Code Signing (Windows EV Certificate)

electron-builder expects a code-signing certificate for production releases. Set these environment variables:

```bash
# For NSIS (.exe)
export CSC_LINK="path/to/certificate.pfx"
export CSC_KEY_PASSWORD="certificate-password"

# For MSIX (.appx)
export WIN_CSC_LINK="path/to/certificate.pfx"
export WIN_CSC_KEY_PASSWORD="certificate-password"
```

Without these, the build succeeds but the .exe is unsigned. Signed builds are recommended for Windows SmartScreen bypass.

### Custom URL Scheme

The app registers the `racecor-prodrive://` protocol. When a user clicks a `racecor-prodrive://callback?code=...` link (e.g., from Discord OAuth), Windows passes it to the running Electron app via `app.on('open-url')`. The app sends it to the renderer via IPC (`deep-link` channel).

Verify registration:

```powershell
Get-ItemProperty Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.racecor-prodrive\UserChoice
```

(Or check HKEY_CLASSES_ROOT\racecor-prodrive on a system-wide install.)

## Architecture Notes

### Single-Instance Lock (Critical on Windows)

`app.requestSingleInstanceLock()` ensures only one instance runs. On a second invocation (e.g., OAuth callback), the first instance receives the deep-link and brings itself to the foreground. Without this, OAuth fails because the window is not focused.

### Auto-Updater

electron-updater is configured to check GitHub Releases for new versions. On first run and at startup, it polls for updates asynchronously.

```javascript
if (!isDev) {
  autoUpdater.checkForUpdatesAndNotify();
}
```

**Note:** Auto-updates work for NSIS .exe but NOT for MSIX (Store manages updates itself).

### Window State Persistence

`window-state.js` saves/restores window position and size to `%APPDATA%/RaceCor Pro Drive/window-state.json`.

### Protocol Handler Registration

On Windows, Electron automatically registers the custom protocol at build time via the `build.protocols` config. The handler is invoked by the OS when a `racecor-prodrive://` link is clicked.

## Microsoft Store (MSIX Submission)

To submit to the Microsoft Store:

1. Generate an MSIX via `npm run build:win` (already configured).
2. Create a Partner Center account (https://partner.microsoft.com).
3. Reserve app name and create a store listing.
4. Upload the MSIX and configure:
   - Screenshots
   - Description
   - Publisher display name: "K10 Motorsports"
   - Pricing (free)
5. Submit for certification.

The app will then be available via the Microsoft Store app, with automatic updates managed by the Store.

## Environment Variables

- `RACECOR_URL` — Override the default URL (default: `https://prodrive.racecor.io`)
- `NODE_ENV` — Set to `development` to enable DevTools and skip auto-updates

## Project Structure

```
racecor-prodrive-win-shell/
├── src/main/
│   ├── index.js          # Electron main process
│   ├── preload.js        # Context bridge (IPC + API exposure)
│   └── window-state.js   # Window bounds persistence
├── assets/
│   ├── icon.ico          # NSIS + exe icon (multi-resolution)
│   └── icon.png          # App icon source (1024x1024)
├── package.json          # electron, electron-builder, electron-updater
├── README.md             # This file
└── .gitignore
```

## Testing

Manual steps:

1. **Dev mode:** `npm run dev` — should open BrowserWindow with DevTools.
2. **OAuth callback:** While running, click a `racecor-prodrive://callback?code=TEST` link from another app. The window should come to focus.
3. **Build:** `npm run build:win` — should produce .exe and .msix in `dist/`.
4. **Install:** Double-click the .exe, follow installer prompts, launch from Start menu.

## CI/CD

CI workflows (e.g., GitHub Actions) should:

1. Checkout the repo
2. `npm install`
3. `npm run build:win` (optionally with `CSC_LINK` and `CSC_KEY_PASSWORD` for signing)
4. Upload artifacts (`dist/*.exe`, `dist/*.msix`) to a release or store

See `.github/workflows/build-win.yml` (to be created).

## Known Limitations

- **ARM/non-x64 Windows:** Currently only targets x64. Add `arm64` to `build.win.target[].arch` if needed.
- **Updater on Store:** MSIX updates are managed by the Microsoft Store, not electron-updater.
- **Code signing:** Requires a valid Windows EV certificate. Without it, SmartScreen may warn users.

## License

Copyright (c) 2026 K10 Motorsports / Kevin Conboy. All rights reserved.
