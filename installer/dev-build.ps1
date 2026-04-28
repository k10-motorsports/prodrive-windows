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
#   .\dev-build.ps1                     # auto-detects shared folder + arch
#   .\dev-build.ps1 -Source "Z:\path"   # override if Parallels mounts elsewhere
#   .\dev-build.ps1 -SkipBuild          # just relaunch the last build

param(
  [string]$Source = "",
  [string]$BuildRoot = "C:\racecor-build",
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# ----Locate the source on the Parallels share--------------------------
if (-not $Source) {
  $candidates = @(
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
$objDir     = Join-Path $BuildRoot "obj\"
$binDir     = Join-Path $BuildRoot "bin\"
$publishDir = Join-Path $BuildRoot "publish\$rid"
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

if (-not $SkipBuild) {
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
    /p:BaseIntermediateOutputPath=$objDir `
    /p:OutputPath=$binDir `
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
