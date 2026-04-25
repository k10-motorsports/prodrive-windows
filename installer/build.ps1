# ─────────────────────────────────────────────────────────────────
# build.ps1 — produce the RaceCor Pro Drive installer.
#
# Orchestrates the two builds and feeds them into Inno Setup:
#   1. Publish the WinUI host (dotnet publish, win-x64, self-contained).
#   2. Build the Electron HUD (electron-builder --win --x64 --dir).
#   3. Run ISCC against installer/RaceCorProDrive.iss with the two
#      output paths injected via /D defines.
#
# Usage:
#   pwsh -File installer/build.ps1            # release, both repos
#   pwsh -File installer/build.ps1 -SkipHud   # host-only smoke test
#
# Assumes the two repos are siblings on disk:
#   <root>/prodrive-windows/
#   <root>/prodrive-overlay/
# Override with -OverlayRepo if your layout differs.
# ─────────────────────────────────────────────────────────────────

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OverlayRepo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..\prodrive-overlay')),
    [switch] $SkipHud,
    [switch] $SkipHost
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$hostProj = Join-Path $repoRoot 'apps\native\src\RaceCorProDrive\RaceCorProDrive.csproj'
$hudDir   = $OverlayRepo

$publishDir = Join-Path $repoRoot "apps\native\src\RaceCorProDrive\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"
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

# ── 3. Inno Setup ──
Write-Host "→ Running ISCC…" -ForegroundColor Cyan
$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if (-not $iscc) {
    $iscc = Resolve-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -ErrorAction SilentlyContinue
}
if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php and ensure ISCC.exe is on PATH."
}

$iss = Join-Path $PSScriptRoot 'RaceCorProDrive.iss'
& $iscc $iss "/DHOST_PUBLISH=$publishDir" "/DHUD_UNPACKED=$hudUnpacked" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$outDir = Join-Path $PSScriptRoot 'output'
Write-Host "✓ Installer written to $outDir" -ForegroundColor Green
Get-ChildItem $outDir -Filter '*.exe' | Format-Table Name, @{Name='Size'; Expression={[math]::Round($_.Length / 1MB, 1).ToString() + ' MB'}}
