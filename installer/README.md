# RaceCor Pro Drive — Installer

Single Inno Setup script that bundles the WinUI 3 host **and** the Electron HUD into one installer. Two repos at source, one app at runtime.

## Layout produced on the user's machine

```
%LOCALAPPDATA%\Programs\RaceCorProDrive\
├── RaceCorProDrive.exe          ← only thing the user double-clicks
├── (host runtime files)
└── Overlay\
    ├── RaceCorOverlay.exe       ← spawned as a child by the host
    └── (Electron runtime files)
```

The host's `OverlayLauncher` resolves `Overlay\RaceCorOverlay.exe` relative to its own `AppContext.BaseDirectory` — no registry lookup, no env var, no first-run wizard.

## Build

```powershell
pwsh -File installer/build.ps1
```

What it does:
1. `dotnet publish` the WinUI host (Release, win-x64, self-contained).
2. `npm install && npx electron-builder --win --x64 --dir` in the sibling `racecorio-prodrive/racecor-overlay/` repo to produce an unpacked tree.
3. Runs `ISCC.exe` against `RaceCorProDrive.iss` with both output paths injected via `/D` defines.
4. Writes `output/RaceCorProDrive-Setup-<version>.exe`.

The HUD lives in a sibling repo by default (`../racecorio-prodrive/racecor-overlay/`); override via `-OverlayRepo <path>` if your checkout looks different. CI will need both repos cloned next to each other.

## Per-user install, no admin

The .iss script uses `PrivilegesRequired=lowest` and `DefaultDirName={localappdata}\Programs\RaceCorProDrive`. UAC never prompts. Anti-virus never freaks out about HKLM writes.

## Settings persistence across upgrades

`%APPDATA%\RaceCor.io\overlay-settings.json` (the file the host writes and the HUD reads) is **not** in the installed tree, so reinstalls / upgrades / uninstalls don't touch it. Users keep their tuned layout forever.

## Why Inno over electron-builder's NSIS

electron-builder is great at packaging Electron apps but it doesn't know about WinUI binaries. Letting Inno Setup combine both prebuilt trees gives us:
- One installer entry in Add/Remove Programs (instead of two separate ones)
- Shared install location (the host can find the HUD at a fixed relative path)
- One uninstaller that takes both products out cleanly

## Future work

- **Code signing**: add `SignTool` directives to the .iss once we have an EV cert.
- **Auto-update**: Inno doesn't ship updates automatically. When we want them, the host can poll a manifest URL and download the next installer; or we add a Squirrel-equivalent updater.
- **MSIX**: when we want Microsoft Store presence, the same two prebuilt trees can feed an MSIX packaging script. The host's path-resolution logic doesn't need to change.
