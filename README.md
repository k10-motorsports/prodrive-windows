# RaceCor Pro Drive — Windows

Two Windows apps for [RaceCor Pro Drive](https://prodrive.racecor.io). One repo, two `apps/*` entries.

| App | Path | Stack |
|---|---|---|
| Shell | `apps/shell/` | Electron — wraps `prodrive.racecor.io` in a signed `.exe` / MSIX with its own Start menu and taskbar icon. Auth is the standard web Discord flow inside a `BrowserWindow`. Custom URL scheme `racecor-prodrive://` registered so OAuth deep links reach the right instance. |
| Native | `apps/native/` | WinUI 3 + C# .NET 8. Fully native shell (`NavigationView`, XAML pages), tokens stored in `PasswordVault`, PKCE Discord via system browser + protocol handler. |

Each app has its own `README.md`, `CLAUDE.md`, and `next-steps.md`.

## Why both?

Shell for speed, native for polish. Shell ships without waiting for feature parity; native catches up with real Windows integrations (toast notifications, jump list, taskbar badges, potential Widgets Board entry).

## Repo layout

```
racecor-prodrive-windows/
├── apps/
│   ├── shell/               Electron Windows app
│   └── native/              WinUI 3 Windows app
├── README.md
└── CLAUDE.md
```

Cross-repo docs live in the parent directory:
- `../native-apps-plan.md`
- `../native-apps-api-contract.md`

## Windows signing / Store paths

Two distribution paths per app:

**Shell (Electron)**
- NSIS installer signed with an EV cert via `signtool` (set `CSC_LINK` + `CSC_KEY_PASSWORD` for `electron-builder`).
- Optional MSIX for Microsoft Store via Partner Center — `electron-builder` has an `appx` target, but the first submission needs Partner Center identity and publisher name registration.

**Native (WinUI 3)**
- Starts **unpackaged** for dev (`dotnet build` + `dotnet run`). Registers `racecor-prodrive://` via the included `assets/register-scheme.reg` for the dev build path.
- For release: flip `WindowsPackageType` to `MSIX` in the `.csproj`, create an App Package in Visual Studio, sign with Partner Center identity, submit to Microsoft Store.

You mentioned you'll handle certs. Bundle identifiers baked in:
- Shell: `racing.k10motorsports.prodrive.racecor.winshell`
- Native: `racing.k10motorsports.prodrive.racecor.win`

## Working on one vs both

Each app has its own toolchain — Node.js 20 + npm for the shell, Visual Studio 2022 17.8+ (or `dotnet` CLI) with the Windows App SDK workload for the native.
