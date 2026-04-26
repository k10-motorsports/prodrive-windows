; RaceCor Pro Drive — combined Windows installer.
; Bundles two repos' build outputs into one self-contained installer:
;   - src/RaceCorProDrive (WinUI 3 host — the only thing the user launches)
;   - racecor-overlay     (Electron HUD — spawned as a child by the host)
;
; Both products live under the same install root so the host can
; resolve the HUD via Path.Combine(AppContext.BaseDirectory, "Overlay",
; "RaceCorOverlay.exe") without registry lookups or env-var dances.
;
; Pre-requisites before running ISCC.exe on this script:
;   1. Build the host:    src/RaceCorProDrive/  → publishes to bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/
;   2. Build the HUD:     ../prodrive-overlay/  → electron-builder packs to dist/win-unpacked/
;   3. Set HOST_PUBLISH and HUD_UNPACKED defines below to those two paths.
;
; The build orchestration lives in `installer/build.ps1` (sibling to
; this file). CI calls that script which runs the two builds, then
; runs ISCC.exe on this script to produce the final .exe.

#define MyAppName        "RaceCor Pro Drive"
#define MyAppShortName   "RaceCorProDrive"
#define MyAppPublisher   "K10 Motorsports"
#define MyAppURL         "https://prodrive.racecor.io"
#define MyAppExeName     "RaceCorProDrive.exe"
#define HudExeName       "RaceCorOverlay.exe"

; Version — driven by the git tag in CI, falls back for local builds.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define MyAppVersion AppVersion

; Paths — overridden on the command line via /D flags from build.ps1
; (local) or the CI release workflow (CI). PLUGIN_UNPACKED is optional;
; if unset the [Files] section skips the Plugin\ payload.
#ifndef HOST_PUBLISH
  #define HOST_PUBLISH "..\src\RaceCorProDrive\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef HUD_UNPACKED
  #define HUD_UNPACKED "..\..\prodrive-overlay\dist\win-unpacked"
#endif

[Setup]
; Per-user install under %LOCALAPPDATA%\Programs\RaceCor — no admin
; prompt, no UAC, matches the macOS "user-space install" experience.
; The host's bundled-binary lookup expects this exact tree.
AppId={{D6F2E0F3-7B6E-4C42-9FE6-3F4E4E5C0DBB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/support
AppUpdatesURL={#MyAppURL}/downloads
DefaultDirName={localappdata}\Programs\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=output
OutputBaseFilename=RaceCorProDrive-Setup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\RaceCorProDrive\Assets\icon.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
CloseApplications=force

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; ── Host (WinUI 3) ────────────────────────────────────────────
; Built from src/RaceCorProDrive/. Drops alongside the Overlay\
; subfolder so the launcher's AppContext.BaseDirectory + "Overlay\..."
; resolution works.
Source: "{#HOST_PUBLISH}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; ── HUD (Electron) ────────────────────────────────────────────
; The whole electron-builder unpacked tree goes under Overlay\.
; RaceCorOverlay.exe + Electron resources + native modules all
; ship together; the host doesn't peek inside.
Source: "{#HUD_UNPACKED}\*"; DestDir: "{app}\Overlay"; Flags: ignoreversion recursesubdirs createallsubdirs

; ── Plugin (SimHub + Homebridge) ──────────────────────────────
; Optional; only included when PLUGIN_UNPACKED is defined (set by
; the CI release workflow after downloading the latest plugin
; release). Local build.ps1 runs leave it out.
#ifdef PLUGIN_UNPACKED
Source: "{#PLUGIN_UNPACKED}\*"; DestDir: "{app}\Plugin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
; Start-menu shortcut is unconditional — there's no good reason to hide
; the app from search. Desktop shortcut stays opt-in.
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings are intentionally NOT removed on uninstall — users almost
; always reinstall after a bad release and would be furious if their
; tuned HUD layout disappeared. They can delete the folder manually.
Type: filesandordirs; Name: "{app}"

[Code]
// Find SimHub install dir by checking common locations. SimHub doesn't
// register a single canonical path so we probe.
function FindSimHubDir(): String;
var
  candidates: array of String;
  i: Integer;
begin
  Result := '';
  SetArrayLength(candidates, 5);
  candidates[0] := ExpandConstant('{pf}\SimHub');
  candidates[1] := ExpandConstant('{pf32}\SimHub');
  candidates[2] := ExpandConstant('{commonpf}\SimHub');
  candidates[3] := ExpandConstant('{commonpf32}\SimHub');
  candidates[4] := ExpandConstant('{localappdata}\Programs\SimHub');
  for i := 0 to GetArrayLength(candidates) - 1 do
  begin
    if FileExists(candidates[i] + '\SimHubWPF.exe') then
    begin
      Result := candidates[i];
      Exit;
    end;
  end;
end;

// Inno Setup helper: collect every (non-dir) file directly under `dir`.
function ListPluginFiles(dir: String; var outFiles: TArrayOfString): Boolean;
var
  fr: TFindRec;
begin
  Result := False;
  SetArrayLength(outFiles, 0);
  if FindFirst(dir + '\*', fr) then
  begin
    try
      repeat
        if (fr.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          SetArrayLength(outFiles, GetArrayLength(outFiles) + 1);
          outFiles[GetArrayLength(outFiles) - 1] := dir + '\' + fr.Name;
        end;
      until not FindNext(fr);
      Result := GetArrayLength(outFiles) > 0;
    finally
      FindClose(fr);
    end;
  end;
end;

// Copy every file from {app}\Plugin into the SimHub install dir so
// SimHub auto-loads RaceCorProDrive.dll on next start. Runs after the
// main file copy so {app}\Plugin already exists.
procedure InstallSimHubPlugin();
var
  simHub: String;
  pluginSrc: String;
  files: TArrayOfString;
  i: Integer;
  dest: String;
begin
  pluginSrc := ExpandConstant('{app}\Plugin');
  if not DirExists(pluginSrc) then Exit;
  simHub := FindSimHubDir();
  if simHub = '' then
  begin
    MsgBox('SimHub was not detected on this machine.' + #13#10 + #13#10 +
           'The RaceCor SimHub plugin DLLs are at:' + #13#10 + pluginSrc + #13#10 + #13#10 +
           'After installing SimHub, copy those files into the SimHub install folder ' +
           '(next to SimHubWPF.exe) to enable the plugin.',
           mbInformation, MB_OK);
    Exit;
  end;
  if not ListPluginFiles(pluginSrc, files) then Exit;
  for i := 0 to GetArrayLength(files) - 1 do
  begin
    dest := simHub + '\' + ExtractFileName(files[i]);
    FileCopy(files[i], dest, False);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then InstallSimHubPlugin();
end;
