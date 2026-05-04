# ─────────────────────────────────────────────────────────────────
# build.ps1 — produce the RaceCor Pro Drive installer.
#
# Orchestrates the builds and feeds them into Inno Setup:
#   1. Publish the WinUI host (dotnet publish, win-x64, self-contained).
#   2. Build the Electron HUD (electron-builder --win --x64 --dir).
#   3. Stage the Chrome extension from prodrive-server (optional —
#      skipped if the repo isn't a sibling).
#   4. Run ISCC against installer/RaceCorProDrive.iss with the
#      output paths injected via /D defines.
#
# Usage:
#   pwsh -File installer/build.ps1            # release, all repos
#   pwsh -File installer/build.ps1 -SkipHud   # host-only smoke test
#
# Assumes the repos are siblings on disk:
#   <root>/prodrive-windows/
#   <root>/prodrive-overlay/
#   <root>/prodrive-server/   (optional — for the Chrome extension)
# Override with -OverlayRepo / -ServerRepo if your layout differs.
# ─────────────────────────────────────────────────────────────────

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OverlayRepo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..\prodrive-overlay')),
    [string] $ServerRepo    = (Join-Path $PSScriptRoot '..\..\prodrive-server'),
    [switch] $SkipHud,
    [switch] $SkipHost,
    [switch] $SkipExtension
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$hostProj = Join-Path $repoRoot 'src\RaceCorProDrive\RaceCorProDrive.csproj'
$hudDir   = $OverlayRepo

$publishDir = Join-Path $repoRoot "src\RaceCorProDrive\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"
$hudUnpacked = Join-Path $hudDir 'dist\win-unpacked'

# ── 1. Host (WinUI 3) ──
if (-not $SkipHost) {
    Write-Host "→ Publishing host…" -ForegroundColor Cyan
    dotnet publish $hostProj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

# ── 2. HUD (Electron) ──
if (-not $SkipHud) {
    Write-Host "→ Building HUD via electron-builder…" -ForegroundColor Cyan
    Push-Location $hudDir
    try {
        # `--dir` produces an unpacked tree we can hand straight to
        # Inno Setup; we don't need electron-builder's own NSIS output
        # because Inno wraps the whole thing.
        npm install        | Out-Host
        npx electron-builder --win --x64 --dir | Out-Host
    } finally {
        Pop-Location
    }
}

# ── 3. Chrome extension (optional) ──
# prodrive-server's apps/web/browser-extension/ is the Manifest V3
# source tree. Two paths the installer can take:
#
#   (a) Unpacked tree — bundled at {app}\ChromeExtension\unpacked\.
#       Powers the host's Load-unpacked card. Always staged when the
#       sibling repo is present.
#
#   (b) Signed .crx + extension ID + version — passed through to
#       [Registry] in the .iss so Chrome offers to install on next
#       launch. Requires CI to have packed and dropped the .crx;
#       local builds skip this and fall back to (a) only.
#
# CI sets EXTENSION_CRX_FILE / EXTENSION_ID / EXTENSION_VERSION
# environment variables before invoking this script (see
# .github/workflows/release.yml). Local runs leave them unset and
# only ship the unpacked variant.
$extensionSrc = $null
$extensionCrx = $env:EXTENSION_CRX_FILE
$extensionId  = $env:EXTENSION_ID
$extensionVer = $env:EXTENSION_VERSION
if (-not $SkipExtension) {
    $serverExtDir = $null
    if (Test-Path $ServerRepo) {
        $resolved = Resolve-Path $ServerRepo
        $candidate = Join-Path $resolved 'apps\web\browser-extension'
        if (Test-Path (Join-Path $candidate 'manifest.json')) {
            $serverExtDir = $candidate
        }
    }
    if ($serverExtDir) {
        Write-Host "→ Staging Chrome extension (unpacked) from $serverExtDir…" -ForegroundColor Cyan
        $extensionSrc = Join-Path $PSScriptRoot 'staging\ChromeExtension'
        if (Test-Path $extensionSrc) { Remove-Item -Recurse -Force $extensionSrc }
        New-Item -ItemType Directory -Path $extensionSrc -Force | Out-Null
        # Copy only what the extension ships — manifest, icons, src.
        # node_modules / .git / dotfiles MUST be excluded so the installer
        # stays small.
        Copy-Item -Path (Join-Path $serverExtDir 'manifest.json') -Destination $extensionSrc
        foreach ($sub in @('icons', 'src')) {
            $src = Join-Path $serverExtDir $sub
            if (Test-Path $src) {
                Copy-Item -Path $src -Destination $extensionSrc -Recurse
            }
        }
    } else {
        Write-Host "  (Chrome extension source not found at $ServerRepo\apps\web\browser-extension — skipping unpacked variant)" -ForegroundColor Yellow
    }
    if ($extensionCrx -and (Test-Path $extensionCrx) -and $extensionId -and $extensionVer) {
        Write-Host "→ Bundling signed Chrome extension: $extensionCrx (id=$extensionId, version=$extensionVer)" -ForegroundColor Cyan
    } elseif ($extensionCrx -or $extensionId -or $extensionVer) {
        Write-Host "  (Partial .crx bundle config — need EXTENSION_CRX_FILE + EXTENSION_ID + EXTENSION_VERSION; skipping registry path)" -ForegroundColor Yellow
        $extensionCrx = $null
        $extensionId  = $null
        $extensionVer = $null
    }
}

# ── 4. Inno Setup ──
Write-Host "→ Running ISCC…" -ForegroundColor Cyan
$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if (-not $iscc) {
    $iscc = Resolve-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -ErrorAction SilentlyContinue
}
if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php and ensure ISCC.exe is on PATH."
}

$iss = Join-Path $PSScriptRoot 'RaceCorProDrive.iss'
$isccArgs = @($iss, "/DHOST_PUBLISH=$publishDir", "/DHUD_UNPACKED=$hudUnpacked")
if ($extensionSrc) {
    $isccArgs += "/DEXTENSION_SRC=$extensionSrc"
}
if ($extensionCrx -and $extensionId -and $extensionVer) {
    $isccArgs += "/DEXTENSION_CRX=$extensionCrx"
    $isccArgs += "/DEXTENSION_ID=$extensionId"
    $isccArgs += "/DEXTENSION_VERSION=$extensionVer"
}
& $iscc @isccArgs | Out-Host
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$outDir = Join-Path $PSScriptRoot 'output'
Write-Host "✓ Installer written to $outDir" -ForegroundColor Green
Get-ChildItem $outDir -Filter '*.exe' | Format-Table Name, @{Name='Size'; Expression={[math]::Round($_.Length / 1MB, 1).ToString() + ' MB'}}
