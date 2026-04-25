# RaceCor Pro Drive Windows Shell — Next Steps

## Icon Art (HIGH PRIORITY)

1. **Source Design** — Create 1024×1024 PNG in Adobe Illustrator or Figma
   - Use AppID branding: `racing.k10motorsports.prodrive.racecor.winshell`
   - Ensure sharp appearance at 16×16 through 256×256 scales
   - Transparent background
   
2. **Export Steps**
   - Save as `assets/icon.png` (1024×1024, RGBA PNG)
   - Convert to .ico: `convert assets/icon.png -define icon:auto-resize=256,128,96,64,48,32,16 assets/icon.ico`
   - Or use an icon editor (e.g., IconBuilder, icoFX)
   
3. **Test** — Run `npm run build:win` and inspect the resulting .exe icon (right-click → Properties → Details tab)

## Code Signing Certificate (PRODUCTION)

1. **Obtain EV Certificate**
   - Provider: DigiCert, Sectigo, GlobalSign, Comodo (Windows kernel-mode code signing)
   - Cost: ~$300–500 USD/year
   - Timeline: 1–2 weeks (identity verification required)
   - Recommended: DigiCert EV (best for SmartScreen bypass)

2. **Export to .pfx**
   - Certificate must include private key
   - Format: PKCS#12 (.pfx)
   
3. **Set Environment Variables**
   ```bash
   export CSC_LINK="/path/to/certificate.pfx"
   export CSC_KEY_PASSWORD="your-password"
   npm run build:win
   ```

4. **Verify Signature**
   ```powershell
   # In PowerShell (admin)
   Get-AuthenticodeSignature dist/RaceCorProDrive.exe
   # Should show "Valid" and issuer name
   ```

**Impact:** Signed builds bypass Windows SmartScreen warnings and increase user trust.

## Microsoft Store (MSIX) Submission

1. **Partner Center Account**
   - Go to https://partner.microsoft.com
   - Sign in with your Microsoft account (or create one)
   - Create a developer account (may require payment)

2. **Reserve App Name**
   - Search for "RaceCor Pro Drive" in the Store to check availability
   - Reserve the name in Partner Center

3. **Create Store Listing**
   - App name: "RaceCor Pro Drive"
   - Publisher display name: "K10 Motorsports"
   - Category: "Entertainment" or "Sports"
   - Description, screenshots, privacy policy, support URL
   - Pricing: Free

4. **Upload MSIX**
   - Run `npm run build:win` to generate `dist/RaceCor Pro Drive 1.0.0.msix`
   - Upload to Partner Center
   - Configure screenshot(s) — at least one 1920×1080 PNG showing the app running

5. **Certification**
   - Submit for review (~24–48 hours)
   - Microsoft tests for malware, crashes, functionality
   - Once approved, appears in Microsoft Store

**Note:** MSIX must be signed with a valid certificate. The electron-builder config will use `CSC_LINK` / `CSC_KEY_PASSWORD` if set.

## Auto-Updater Infrastructure (OPTIONAL)

1. **GitHub Actions Workflow** (e.g., `.github/workflows/build-win.yml`)
   ```yaml
   name: Build Windows
   on:
     push:
       tags: ['v*']
   jobs:
     build:
       runs-on: windows-latest
       steps:
         - uses: actions/checkout@v3
         - uses: actions/setup-node@v3
           with: { node-version: 18 }
         - run: npm install && npm run build:win
           env:
             CSC_LINK: ${{ secrets.WIN_CSC_LINK }}
             CSC_KEY_PASSWORD: ${{ secrets.WIN_CSC_KEY_PASSWORD }}
         - uses: actions/create-release@v1
           with:
             tag_name: ${{ github.ref }}
             files: dist/*.exe
   ```

2. **GitHub Secrets**
   - Store certificate as base64-encoded secret: `WIN_CSC_LINK`
   - Store password as secret: `WIN_CSC_KEY_PASSWORD`

3. **Tagging**
   - Push a tag: `git tag -a v1.0.0 -m "Release 1.0.0" && git push origin v1.0.0`
   - GitHub Actions builds and uploads .exe to Releases
   - electron-updater detects it automatically

## Windows Jump List (NICE-TO-HAVE)

Add recent sessions or quick-action shortcuts to the taskbar:

```javascript
// In src/main/index.js, after window creation:
app.setUserTasks([
  {
    program: process.execPath,
    arguments: '--new-window',
    iconPath: path.join(__dirname, '..', '..', 'assets', 'icon.ico'),
    iconIndex: 0,
    title: 'New Window'
  },
  {
    program: process.execPath,
    arguments: 'racecor-prodrive://session/latest',
    iconPath: path.join(__dirname, '..', '..', 'assets', 'icon.ico'),
    iconIndex: 0,
    title: 'Resume Latest Session'
  }
]);
```

## Native Notifications (NICE-TO-HAVE)

Integrate Windows 10+ native toast notifications for update prompts:

```javascript
const { Notification } = require('electron');

new Notification({
  title: 'Update Available',
  body: 'A new version of RaceCor Pro Drive is ready. Restart to apply.',
  icon: path.join(__dirname, '..', '..', 'assets', 'icon.png')
}).show();
```

## Checklist

- [ ] Icon art (1024×1024 PNG + .ico)
- [ ] Code signing certificate (EV cert from DigiCert/Sectigo)
- [ ] Environment variables set (CSC_LINK, CSC_KEY_PASSWORD)
- [ ] Test build: `npm run build:win`
- [ ] Microsoft Partner Center account created
- [ ] App name reserved
- [ ] Store listing configured (description, screenshots, publisher)
- [ ] MSIX uploaded and certified
- [ ] GitHub Actions workflow for releases (optional)
- [ ] Auto-updater tested (optional)
- [ ] Taskbar jump list (optional, nice-to-have)
- [ ] Native notifications (optional, nice-to-have)

## References

- [electron-builder Windows Target](https://www.electron.build/configuration/win)
- [electron-builder NSIS Target](https://www.electron.build/configuration/nsis)
- [electron-builder MSIX Target](https://www.electron.build/configuration/appx)
- [electron-updater GitHub Provider](https://www.electron.build/auto-update)
- [Microsoft Partner Center](https://partner.microsoft.com)
- [Windows Code Signing](https://docs.microsoft.com/en-us/windows/win32/seccrypto/using-signtool-to-sign-a-file)
