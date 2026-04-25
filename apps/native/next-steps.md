# Next Steps — RaceCor Pro Drive Windows App

## v0 (Current) Completion

- [x] Solution structure (WinUI 3 conventions)
- [x] XAML shell (NavigationView, dark theme)
- [x] OAuth 2.0 PKCE flow (AuthService + PkceHelper)
- [x] Secure token storage (PasswordVault)
- [x] HTTP client with 401 refresh + retry (ApiClient)
- [x] LoginPage (Discord sign-in button)
- [x] DashboardPage (stat tiles, placeholder)
- [x] SessionsPage (paginated, category filter, tap to detail)
- [x] SessionDetailPage (header, laps list)
- [x] PlaceholderPage (reusable for 9 nav items)
- [x] Design tokens (K10 red, dark theme)
- [x] README + CLAUDE.md + architecture docs
- [x] Protocol handler registration (.reg file)
- [x] Package.appxmanifest (stub, expandable for MSIX)

## v1 Priorities

### 1. Dashboard Enrichment
When `/api/v1/dashboard` is fully implemented:
- Parse `stats.totalRaces`, `stats.uniqueTracks`, `stats.uniqueCars`, `stats.careerDays`
- Render as grid of InfoBadge-style cards
- Fetch and display recent sessions below
- Add chart placeholder for aggregate metrics (if API provides)

### 2. Moments + DNA + When Pages
Replace PlaceholderPage instances:
- **Moments**: video clips, highlights carousel with metadata
- **DNA**: driver profile, personality metrics, strengths/weaknesses
- **When**: availability calendar, race schedule, practice times

### 3. Windows Theme Integration
- Read `UISettings.GetColorValues()` for accent color
- Apply user's Windows accent to buttons, hover states
- Support light/dark mode switching (currently dark-only)

### 4. Notifications
```csharp
using Windows.UI.Notifications;
// ToastNotificationManager.CreateToastNotifier()
// Notify on: race results, moments tagged, friend activity
```

### 5. Jump List & Taskbar Badge
```csharp
// Jump list: 5 most recent sessions
// Taskbar badge: unread moments count
```

## v2 (Later)

### 6. MSIX Packaging & Microsoft Store
- Flip `WindowsPackageType` to `MSIX` in .csproj
- Obtain Microsoft Partner Center identity (K10 Motorsports org)
- Generate signing certificate (EV cert, see below)
- Package via `dotnet publish -c Release`
- Submit to Store (or distribute direct .msix)

### 7. EV Certificate Procurement
- **Vendor**: DigiCert, Sectigo, GlobalSign (Windows-trusted roots)
- **Cost**: ~$300–500/year
- **Process**: 
  1. Generate CSR in VS (Project Properties → Signing)
  2. Submit to CA with K10 Motorsports entity docs
  3. Receive signed .pfx + install to machine
  4. Configure CI to sign on build
- **CI Integration**: msbuild `/p:CertificatePath=...` or signtool.exe

### 8. Accessibility
- **Narrator support**: ensure XAML controls have meaningful AutomationProperties.Name
- **High-contrast mode**: test and fix color contrast (WCAG AA minimum 4.5:1)
- **Keyboard navigation**: Tab/Shift+Tab, Enter on buttons, arrow keys in lists
- **Screen reader testing**: NVDA (free), JAWS (paid)

### 9. ARM64 Build & Signing
- Csproj already has `RuntimeIdentifiers` with `win-arm64`
- Test on ARM64 hardware (Surface X Pro, etc.) or via emulation
- CI: build both x64 and arm64 in parallel, sign each separately

### 10. Performance Tuning
- Profile with Windows Performance Toolkit (ETW)
- Reduce initial load time (lazy-load pages, defer image loading)
- Optimize API polling (currently 1 request per page view, batch where possible)

## Implementation Checklist for Next Dev

```
Dashboard v1:
- [ ] Fetch /api/v1/dashboard (wait for API to be non-stub)
- [ ] Parse DashboardStats into ObservableCollection
- [ ] Design stat card XAML template
- [ ] Bind recent sessions to ItemsRepeater

Moments/DNA/When:
- [ ] Design Moments carousel (videos, metadata)
- [ ] Design DNA profile panel (stats, chart)
- [ ] Design When calendar/schedule view
- [ ] Each requires new API endpoint

Theme Integration:
- [ ] Read Windows accent color on launch
- [ ] Update ResourceDictionary dynamically
- [ ] Test light/dark mode toggle

Notifications:
- [ ] Set up toast template
- [ ] Subscribe to race result events
- [ ] Test on Windows 10 + 11

MSIX + Store:
- [ ] Get Partner Center account
- [ ] Update app identity in manifest
- [ ] Build signed package
- [ ] Create Store listing

EV Cert:
- [ ] Choose CA vendor
- [ ] Generate CSR
- [ ] Submit with org docs
- [ ] Install .pfx in CI environment
- [ ] Test signed .msix on clean machine

Accessibility:
- [ ] Run accessibility checker
- [ ] Fix color contrast issues
- [ ] Test Narrator navigation
- [ ] Test keyboard-only navigation
- [ ] Screen reader audit with NVDA
```

## Known Limitations / Technical Debt

1. **ApiClient.PostAsync assumes JSON body** — no form-data support yet
2. **No offline mode** — all pages require active internet
3. **No caching layer** — repeat requests fetch fresh data
4. **AuthService protocol handler is manual** — should auto-register on first run
5. **No logging to file** — AppLogger only outputs to debugger
6. **SessionsPage pagination is simple** — no infinite scroll
7. **Design tokens are static** — no theme switching UI yet
8. **PlaceholderPages are stubs** — 9 nav items not yet fully designed

## Build & Test Workflow

```bash
# Daily development
dotnet build
dotnet test  # (no tests yet, add to /Tests/)

# Before commit
# - Manual test on unpackaged app (F5)
# - Protocol handler test: explorer racecor-prodrive://auth?code=test&state=test
# - API test: sign in, navigate to Sessions, verify data loads

# Release candidate
dotnet publish -c Release -o publish/
# Test published .exe
# Sign (once EV cert is ready)

# MSIX submission
# Right-click project → Publish → Create App Packages
# (requires Partner Center account)
```

## References

- [WinUI 3 documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [PKCE flow (RFC 7636)](https://tools.ietf.org/html/rfc7636)
- [PasswordVault docs](https://learn.microsoft.com/uwp/api/windows.security.credentials.passwordvault)
- [MSIX packaging](https://learn.microsoft.com/windows/msix/)
- [EV Code Signing](https://learn.microsoft.com/windows/win32/seccrypto/using-signtool-to-sign-a-file)
