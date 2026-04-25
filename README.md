# RaceCor Pro Drive — Windows

The Windows native app for [RaceCor Pro Drive](https://prodrive.racecor.io). Lives under `apps/native/` so a future second target can sit alongside it without a restructure.

| App | Path | Stack |
|---|---|---|
| Native | `apps/native/` | WinUI 3 + C# .NET 8. Fully native shell (`NavigationView`, XAML pages), tokens stored in `PasswordVault`, PKCE Discord via system browser + protocol handler. |

The app has its own `README.md`, `CLAUDE.md`, and `next-steps.md`.

## Repo layout

```
racecor-prodrive-windows/
├── apps/
│   └── native/              WinUI 3 Windows app
├── README.md
└── CLAUDE.md
```

Cross-repo docs live in the parent directory:
- `../native-apps-plan.md`
- `../native-apps-api-contract.md`

## Windows signing / Store path

- Starts **unpackaged** for dev (`dotnet build` + `dotnet run`). Registers `racecor-native://` via the included `assets/register-scheme.reg` for the dev build path.
- For release: flip `WindowsPackageType` to `MSIX` in the `.csproj`, create an App Package in Visual Studio, sign with Partner Center identity, submit to Microsoft Store.

You mentioned you'll handle certs. Bundle identifier baked in:
- Native: `racing.k10motorsports.prodrive.racecor.win`

## Working on the app

Toolchain: Visual Studio 2022 17.8+ (or `dotnet` CLI) with the Windows App SDK workload.
