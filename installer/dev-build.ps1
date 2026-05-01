# dev-build.ps1 - local debug-build loop for the WinUI 3 host.
#
# Builds the host straight from your Mac source via Parallels' shared
# folder, then launches the .exe. ~30 sec per cycle vs. 5 min through CI.
#
# Skips the Inno Setup installer, overlay download, and plugin download -
# those aren't needed to verify the host launches and renders a window.
# Re-run the full CI pipeline (push a v* tag) when you want a real
# user-facing release.
#
# Usage (from PowerShell in the Win11 VM):
#   .\dev-build.ps1                       # auto-detects shared folder + arch
#   .\dev-build.ps1 -Source "Z:\path"     # override if Parallels mounts elsewhere
#   .\dev-build.ps1 -SkipBuild            # just relaunch the last build
#   .\dev-build.ps1 -IncludeOverlay       # stage prodrive-overlay next to the
#                                         # host so the bottom-left HUD launcher
#                                         # button appears + works in dev runs.
#                                         # If the overlay was already built on
#                                         # the Mac side (dist/win-unpacked/
#                                         # exists) we just copy it - Electron
#                                         # builds cross-compile via Mac and
#                                         # land much faster than rebuilding in
#                                         # the VM. Pair with -RebuildOverlay to
#                                         # force a fresh in-VM build.
#   .\dev-build.ps1 -IncludeOverlay -RebuildOverlay

param(
  [string]$Source = "",
  [string]$BuildRoot = "C:\racecor-build",
  [string]$OverlayRepo = "",
  [switch]$SkipBuild,
  [switch]$IncludeOverlay,
  [switch]$RebuildOverlay
)

$ErrorActionPreference = "Stop"

# ----Locate the source on the Parallels share--------------------------
if (-not $Source) {
  $candidates = @(
    (Get-Location).Path,                  # if you cd'd into the project dir
    (Split-Path $PSScriptRoot -Parent),   # script lives in installer/, project is parent
    "Z:\Documents\K10\racecor-prodrive\prodrive-windows",
    "\\Mac\Home\Documents\K10\racecor-prodrive\prodrive-windows",
    "\\psf\Home\Documents\K10\racecor-prodrive\prodrive-windows"
  )
  foreach ($c in $candidates) {
    if (Test-Path (Join-Path $c "src\RaceCorProDrive\RaceCorProDrive.csproj")) {
      $Source = $c
      break
    }
  }
}
if (-not $Source) {
  Write-Host "Could not auto-detect the project on a Parallels share." -ForegroundColor Red
  Write-Host "Pass -Source `"<path>`" with the location of prodrive-windows."
  Write-Host "Try running this in the VM to find it:"
  Write-Host '  Get-PSDrive | ? { $_.Provider.Name -eq "FileSystem" }'
  exit 1
}
$proj = Join-Path $Source "src\RaceCorProDrive\RaceCorProDrive.csproj"
Write-Host "Source: $Source" -ForegroundColor Cyan

# ----Locate MSBuild (.NET Framework, from VS)---------------------------
# We discovered the hard way that dotnet's MSBuild causes XamlCompiler.exe
# to silent-crash on WinUI 3 1.5/1.6 unpackaged. VS Build Tools' msbuild
# (.NET Framework) is the only one that works.
$msbuild = $null
$msCandidates = @(
  "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
  "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
  "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
  "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
  "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)
foreach ($c in $msCandidates) {
  if (Test-Path $c) { $msbuild = $c; break }
}
if (-not $msbuild) {
  Write-Host "MSBuild not found (Visual Studio Build Tools required)." -ForegroundColor Red
  Write-Host "Install with:"
  Write-Host '  winget install --id Microsoft.VisualStudio.2022.BuildTools --silent --override "--quiet --add Microsoft.VisualStudio.Workload.MSBuildTools --add Microsoft.NetCore.Component.SDK --add Microsoft.VisualStudio.Component.Roslyn.Compiler --add Microsoft.Net.Component.4.7.2.TargetingPack --includeRecommended"'
  exit 1
}
Write-Host "MSBuild: $msbuild" -ForegroundColor Cyan

# ----Determine arch-----------------------------------------------------
$archEnv = $env:PROCESSOR_ARCHITECTURE
if ($archEnv -eq "ARM64") {
  $msbuildPlatform = "ARM64"
  $rid = "win-arm64"
} else {
  $msbuildPlatform = "x64"
  $rid = "win-x64"
}
Write-Host "Arch: $msbuildPlatform / $rid" -ForegroundColor Cyan

# ----Build to a local Windows path for speed----------------------------
# Network-share filesystems are 5-10x slower for thousands-of-small-files
# I/O. Redirect MSBuild's obj/ and publish/ to a local NTFS path; only
# source reads come from the share.
$publishDir = Join-Path $BuildRoot "publish\$rid"
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

# ----Kill any prior host or overlay process----------------------------
# A leftover RaceCorProDrive.exe (or RaceCorProDriveOverlay.exe) from a
# previous dev cycle holds locks on the DLLs we're about to overwrite,
# which makes 'dotnet publish' fail with MSB3021/MSB3027. Close them
# first; harmless when nothing's running.
foreach ($name in @('RaceCorProDrive', 'RaceCorProDriveOverlay')) {
  $existing = Get-Process -Name $name -ErrorAction SilentlyContinue
  if ($existing) {
    Write-Host "Stopping running '$name' (PID $($existing.Id -join ', '))..." -ForegroundColor Yellow
    $existing | Stop-Process -Force -ErrorAction SilentlyContinue
    # Brief settle so the OS releases file handles before MSBuild
    # tries to write the publish dir.
    Start-Sleep -Milliseconds 500
  }
}

if (-not $SkipBuild) {
  # Side-load System.Security.Permissions 6.0.0 next to the in-process
  # XAML compiler task DLL — without it, MSBuild can't load the task
  # and every InitializeComponent/x:Name reference fails to compile.
  # Mirrors the equivalent step in .github/workflows/release.yml.
  Write-Host "`nRestore + patch XAML compiler task deps..." -ForegroundColor Yellow
  $env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages"
  & dotnet restore $proj -p:RuntimeIdentifier=$rid -p:Platform=$msbuildPlatform | Out-Null
  if ($LASTEXITCODE -ne 0) { Write-Host "restore failed" -ForegroundColor Red; exit 1 }
  $compilerDll = Get-ChildItem -Recurse -Filter "Microsoft.UI.Xaml.Markup.Compiler.dll" -Path "$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "tools\\net6\.0" } |
    Select-Object -First 1
  if (-not $compilerDll) { Write-Host "Compiler DLL not found in nuget cache" -ForegroundColor Red; exit 1 }
  $toolsDir = $compilerDll.Directory.FullName
  $existingSysPerms = Join-Path $toolsDir "System.Security.Permissions.dll"
  if (-not (Test-Path $existingSysPerms)) {
    $sysPerms = Get-ChildItem -Recurse -Filter "System.Security.Permissions.dll" -Path "$env:USERPROFILE\.nuget\packages\system.security.permissions" -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match "6\.0\.0\\lib\\net6\.0" } |
      Select-Object -First 1
    if (-not $sysPerms) {
      Write-Host "Fetching System.Security.Permissions 6.0.0..." -ForegroundColor Yellow
      $tmp = Join-Path $env:TEMP "racecor-syspermfetch"
      New-Item -ItemType Directory -Force -Path $tmp | Out-Null
      "<Project Sdk=""Microsoft.NET.Sdk""><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=""System.Security.Permissions"" Version=""6.0.0"" /></ItemGroup></Project>" | Set-Content (Join-Path $tmp "tmp.csproj")
      Push-Location $tmp
      & dotnet restore tmp.csproj | Out-Null
      Pop-Location
      $sysPerms = Get-ChildItem -Recurse -Filter "System.Security.Permissions.dll" -Path "$env:USERPROFILE\.nuget\packages\system.security.permissions" |
        Where-Object { $_.FullName -match "6\.0\.0\\lib\\net6\.0" } |
        Select-Object -First 1
    }
    if (-not $sysPerms) { Write-Host "Could not obtain System.Security.Permissions 6.0.0" -ForegroundColor Red; exit 1 }
    Copy-Item -Force $sysPerms.FullName $toolsDir
    Write-Host "Patched: $toolsDir\System.Security.Permissions.dll" -ForegroundColor Green
  }

  Write-Host "`nBuilding..." -ForegroundColor Yellow
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  & $msbuild $proj `
    /t:Restore`;Publish `
    /p:Configuration=Release `
    /p:Platform=$msbuildPlatform `
    /p:RuntimeIdentifier=$rid `
    /p:SelfContained=true `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=false `
    /p:PublishDir=$publishDir `
    /p:UseXamlCompilerExecutable=false `
    /v:minimal `
    /nologo
  $sw.Stop()
  if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBUILD FAILED ($LASTEXITCODE) after $($sw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Red
    exit 1
  }
  Write-Host "Build OK in $($sw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
}

# ----Optional: build the Electron overlay and stage it----------------
# When -IncludeOverlay is passed, run electron-builder in the sibling
# prodrive-overlay repo and copy its win-unpacked output into
# <publishDir>\Overlay\. The host's OverlayLauncher resolves
# `{BaseDir}\Overlay\RaceCorOverlay.exe` first; staging it here is what
# makes the bottom-left HUD launcher button visible + functional in
# dev runs (without it, the FAB hides itself because the binary isn't
# bundled).
if ($IncludeOverlay) {
  if (-not $OverlayRepo) {
    $candidates = @(
      (Join-Path (Split-Path $Source -Parent) 'prodrive-overlay'),
      "Z:\Documents\K10\racecor-prodrive\prodrive-overlay",
      "\\Mac\Home\Documents\K10\racecor-prodrive\prodrive-overlay",
      "\\psf\Home\Documents\K10\racecor-prodrive\prodrive-overlay"
    )
    foreach ($c in $candidates) {
      if (Test-Path (Join-Path $c 'package.json')) { $OverlayRepo = $c; break }
    }
  }
  if (-not $OverlayRepo -or -not (Test-Path (Join-Path $OverlayRepo 'package.json'))) {
    Write-Host "-IncludeOverlay set but prodrive-overlay not found." -ForegroundColor Red
    Write-Host "Pass -OverlayRepo `"<path>`" with the location of the overlay repo." -ForegroundColor Red
    exit 1
  }

  # Prefer a pre-built overlay if one's sitting on disk - typically
  # the user has run 'npm run build:win' on the Mac side, which
  # cross-compiles Windows binaries in seconds vs. the 5-10 min it
  # takes to rebuild inside the Parallels VM.
  #
  # electron-builder lays out per-arch unpacked trees:
  #   dist/win-unpacked/        -> x64 (the "primary" arch electron-builder uses)
  #   dist/win-arm64-unpacked/  -> arm64
  # Pick the directory matching the host arch we're publishing for -
  # otherwise on ARM64 we stage an x64 binary the host can't reliably
  # spawn (or worse, silently rebuilds in the VM for many minutes).
  $unpackedSubdir = if ($rid -eq 'win-arm64') { 'dist\win-arm64-unpacked' } else { 'dist\win-unpacked' }
  $sourceUnpacked = Join-Path $OverlayRepo $unpackedSubdir
  $haveBuilt = Test-Path (Join-Path $sourceUnpacked 'RaceCorProDriveOverlay.exe')
  Write-Host "Overlay arch: $rid  ->  $sourceUnpacked" -ForegroundColor DarkGray
  Write-Host "  RaceCorProDriveOverlay.exe present: $haveBuilt" -ForegroundColor DarkGray

  if ($RebuildOverlay -or -not $haveBuilt) {
    if ($RebuildOverlay) {
      Write-Host "`n-RebuildOverlay set - building overlay in VM..." -ForegroundColor Yellow
    } else {
      Write-Host "`nNo pre-built overlay at $sourceUnpacked - building in VM..." -ForegroundColor Yellow
      Write-Host "  (faster: run 'npm run build:win' in prodrive-overlay on the Mac side)" -ForegroundColor DarkGray
    }

    # Copy overlay sources to a local NTFS path for the same reason
    # we do it for the host: electron-builder + npm install are
    # I/O-heavy and grind on a Parallels share.
    $overlayLocal = Join-Path $BuildRoot 'overlay-src'
    if (Test-Path $overlayLocal) { Remove-Item -Recurse -Force $overlayLocal }
    New-Item -ItemType Directory -Force -Path $overlayLocal | Out-Null
    robocopy $OverlayRepo $overlayLocal /MIR /XD node_modules dist .git tests test-results /NFL /NDL /NJH /NJS /NP /NS | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Host "robocopy failed copying overlay sources" -ForegroundColor Red; exit 1 }

    Push-Location $overlayLocal
    try {
      $owSw = [System.Diagnostics.Stopwatch]::StartNew()
      & npm install --no-audit --no-fund --loglevel=error
      if ($LASTEXITCODE -ne 0) { Write-Host "npm install failed" -ForegroundColor Red; exit 1 }
      $arch = if ($rid -eq 'win-arm64') { '--arm64' } else { '--x64' }
      & npx electron-builder --win $arch --dir
      if ($LASTEXITCODE -ne 0) { Write-Host "electron-builder failed" -ForegroundColor Red; exit 1 }
      $owSw.Stop()
      Write-Host "Overlay build OK in $($owSw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
    } finally {
      Pop-Location
    }

    $sourceUnpacked = Join-Path $overlayLocal 'dist\win-unpacked'
  } else {
    Write-Host "`nUsing pre-built overlay from $sourceUnpacked" -ForegroundColor Green
  }

  if (-not (Test-Path $sourceUnpacked)) {
    Write-Host "Expected overlay at $sourceUnpacked, not found." -ForegroundColor Red
    exit 1
  }
  $overlayDest = Join-Path $publishDir 'Overlay'
  if (Test-Path $overlayDest) { Remove-Item -Recurse -Force $overlayDest }
  New-Item -ItemType Directory -Force -Path $overlayDest | Out-Null
  Write-Host "Staging overlay -> $overlayDest" -ForegroundColor Cyan
  $stageSw = [System.Diagnostics.Stopwatch]::StartNew()
  # Drop /NFL /NDL so we get directory-level progress while the
  # share-to-NTFS robocopy churns; previously this looked frozen
  # for minutes at a time on ARM64 Parallels VMs.
  robocopy $sourceUnpacked $overlayDest /MIR /NJH /NJS /NP /NS
  if ($LASTEXITCODE -ge 8) { Write-Host "robocopy failed staging overlay" -ForegroundColor Red; exit 1 }
  $stageSw.Stop()
  $stagedExe = Join-Path $overlayDest 'RaceCorProDriveOverlay.exe'
  if (Test-Path $stagedExe) {
    Write-Host "Staged in $($stageSw.Elapsed.TotalSeconds.ToString('F1'))s - RaceCorProDriveOverlay.exe present" -ForegroundColor Green
  } else {
    Write-Host "Staging finished but RaceCorProDriveOverlay.exe is missing at $stagedExe" -ForegroundColor Red
    Write-Host "Check that 'npm run build:win' actually produced it on the Mac." -ForegroundColor Red
  }
}

# ----Register racecor-native:// URL protocol for OAuth callback-------
# The Inno Setup installer does this for end users; the dev loop needs
# it too or the OAuth round-trip dies after the browser handoff with
# "no app handles this protocol".
$exeForReg = Join-Path $publishDir "RaceCorProDrive.exe"
if (Test-Path $exeForReg) {
  New-Item -Path "HKCU:\Software\Classes\racecor-native" -Force | Out-Null
  Set-ItemProperty -Path "HKCU:\Software\Classes\racecor-native" -Name "(default)" -Value "URL:RaceCor Pro Drive Protocol"
  Set-ItemProperty -Path "HKCU:\Software\Classes\racecor-native" -Name "URL Protocol" -Value ""
  New-Item -Path "HKCU:\Software\Classes\racecor-native\shell\open\command" -Force | Out-Null
  Set-ItemProperty -Path "HKCU:\Software\Classes\racecor-native\shell\open\command" -Name "(default)" -Value "`"$exeForReg`" `"%1`""
  Write-Host "Registered racecor-native:// -> $exeForReg" -ForegroundColor Cyan
}

# ----Tail the boot log in a side window so we see startup live----------
$logsDir = Join-Path $env:LOCALAPPDATA "RaceCorProDrive\Logs"
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
$bootLog = Join-Path $logsDir "boot.log"
$crashLog = Join-Path $logsDir "crash.log"
# Truncate previous run's logs so we only see this run's output
"" | Set-Content $bootLog
if (Test-Path $crashLog) { Remove-Item $crashLog }

# ----Launch-------------------------------------------------------------
$exe = Join-Path $publishDir "RaceCorProDrive.exe"
Write-Host "`nLaunching $exe" -ForegroundColor Yellow
$proc = Start-Process -FilePath $exe -PassThru
Write-Host "PID $($proc.Id)" -ForegroundColor Cyan
Start-Sleep -Seconds 3

# Show what happened
if ($proc.HasExited) {
  Write-Host "`nProcess exited (code=$($proc.ExitCode)) within 3s - likely crashed." -ForegroundColor Red
} else {
  Write-Host "`nProcess still running. Window should be visible." -ForegroundColor Green
}
Write-Host "`n---boot.log---" -ForegroundColor Cyan
if (Test-Path $bootLog) { Get-Content $bootLog } else { Write-Host "(no entries)" }
if (Test-Path $crashLog) {
  Write-Host "`n---crash.log---" -ForegroundColor Red
  Get-Content $crashLog
}
