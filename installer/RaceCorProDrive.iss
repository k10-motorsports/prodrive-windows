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
#define HudExeName       "RaceCorProDriveOverlay.exe"

; Version — driven by the git tag in CI, falls back for local builds.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define MyAppVersion AppVersion

; Architecture — set by CI to "x64" or "arm64" so we ship matching
; installer per arch and Inno Setup correctly rejects mismatched
; targets at install time. Defaults to x64 for local builds.
#ifndef AppArch
  #define AppArch "x64"
#endif

; Paths — overridden on the command line via /D flags from build.ps1
; (local) or the CI release workflow (CI). PLUGIN_UNPACKED is optional;
; if unset the [Files] section skips the Plugin\ payload.
#ifndef HOST_PUBLISH
  #define HOST_PUBLISH "..\src\RaceCorProDrive\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef HUD_UNPACKED
  #define HUD_UNPACKED "..\..\prodrive-overlay\dist\win-unpacked"
#endif
; PLUGIN_UNPACKED expects a tree containing the plugin DLLs at its
; root (RaceCorProDrive.dll + IRSDKSharper/Newtonsoft/etc.) and a
; racecorprodrive-data/ subfolder for the JSON datasets. The plugin
; repo's CI release workflow assembles that layout; for local builds
; we point at the build/ output folder which already contains the
; DLLs, and rely on the data folder being copied alongside it (or on
; the user not needing the data side for installer smoke tests).
;
; If PLUGIN_UNPACKED isn't defined and the path below doesn't exist,
; the [Files] #ifdef below silently skips the plugin payload.

; EXTENSION_SRC: unpacked Chrome extension folder (manifest.json at
; root). When defined, the installer bundles the unpacked tree under
; {app}\ChromeExtension so the host can offer Load-unpacked.
;
; EXTENSION_CRX / EXTENSION_ID / EXTENSION_VERSION: signed .crx file
; + the extension's ID and version. When all three are defined, the
; installer also bundles the .crx and registers it with Chrome and
; Edge via HKCU. Chrome offers to install on next launch (subject to
; per-browser external-extension policy — see CHROME EXTENSION INSTALL
; below). EXTENSION_ID is the deterministic 32-char ID derived from
; the public key the .crx was signed with; EXTENSION_VERSION must
; match the manifest.json version inside the .crx.

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
OutputBaseFilename=RaceCorProDrive-Setup-{#MyAppVersion}-{#AppArch}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\RaceCorProDrive\Assets\icon.ico
#if AppArch == "arm64"
  #define IscArchKeyword "arm64"
#else
  #define IscArchKeyword "x64compatible"
#endif
ArchitecturesAllowed={#IscArchKeyword}
ArchitecturesInstallIn64BitMode={#IscArchKeyword}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
CloseApplications=force

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; ── Optional companion installs (default-on, opt-out from the Tasks page) ──
; SimHub plugin: copies DLLs into the SimHub install dir, see
;   InstallSimHubPlugin() below.
; Stream Deck plugin: copies the .sdPlugin folder into Elgato's
;   plugins dir, see InstallStreamDeckPlugin() below. Only offered
;   when STREAMDECK_PLUGIN points at a real on-disk .sdPlugin folder
;   (the overlay's release CI publishes streamdeck-plugin.zip for
;   exactly this purpose; the asar-buried copy inside HUD_UNPACKED
;   isn't accessible to a post-install copy step).
; Chrome extension: writes HKCU\Software\Google\Chrome\Extensions\<id>
;   so Chrome offers to install on next launch, see [Registry] +
;   notes in CHROME EXTENSION INSTALL.
;
; Each task only fires when its source payload is bundled — uncheck
; to skip entirely. If a task is unticked, the relevant [Files] /
; [Registry] / [Code] hook short-circuits.
Name: "simhubplugin"; Description: "Install the RaceCor SimHub plugin (copies DLLs into the SimHub install folder)"; GroupDescription: "Companion installs:"
#ifdef STREAMDECK_PLUGIN
Name: "streamdeckplugin"; Description: "Install the RaceCor Stream Deck plugin (copies into Elgato's plugins folder)"; GroupDescription: "Companion installs:"
#endif
; The chromeextension task fires whenever ANY extension payload is
; bundled — whether the unpacked tree (for the host's Load-unpacked
; fallback card) or the signed .crx (for the registry-install path)
; or both. Previously the task was only declared when EXTENSION_ID
; was set, so an extension release without a signed .crx
; (e.g. prodrive-server extension-v0.3.1, which shipped only .zip)
; produced [Files] entries that referenced an undeclared task and
; the unpacked payload silently dropped on the floor.
#if defined(EXTENSION_ID) || defined(EXTENSION_SRC)
Name: "chromeextension"; Description: "Install the RaceCor Chrome extension (Chrome will prompt on next launch)"; GroupDescription: "Companion installs:"
#endif
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

; ── Stream Deck plugin (optional) ─────────────────────────────
; Bundled as a real on-disk .sdPlugin folder (NOT extracted from
; the overlay's resources/app.asar). The post-install code below
; copies this into %APPDATA%\Elgato\StreamDeck\Plugins\.
#ifdef STREAMDECK_PLUGIN
Source: "{#STREAMDECK_PLUGIN}\*"; DestDir: "{app}\StreamDeckPlugin\com.k10motorsports.racecor.overlay.sdPlugin"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: streamdeckplugin
#endif

; ── Chrome extension (optional) ───────────────────────────────
; The MV3 "RaceCor iRacing Sync" extension lives in prodrive-server
; at apps/web/browser-extension/. Two payload variants:
;   EXTENSION_SRC  — unpacked tree, for the host's Load-unpacked
;                    fallback flow (always bundled when defined).
;   EXTENSION_CRX  — signed .crx, paired with EXTENSION_ID +
;                    EXTENSION_VERSION so [Registry] can hand it to
;                    Chrome for prompt-and-install on next launch.
;
; CI without prodrive-server set up simply omits the defines and
; skips the relevant payloads — the rest of the installer is fine.
#ifdef EXTENSION_SRC
Source: "{#EXTENSION_SRC}\*"; DestDir: "{app}\ChromeExtension\unpacked"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: chromeextension
#endif
#ifdef EXTENSION_CRX
Source: "{#EXTENSION_CRX}"; DestDir: "{app}\ChromeExtension"; DestName: "racecor-iracing-sync.crx"; Flags: ignoreversion; Tasks: chromeextension
#endif

[Icons]
; Start-menu shortcut is unconditional — there's no good reason to hide
; the app from search. Desktop shortcut stays opt-in.
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; ── .rcpdv (RaceCor Pro Drive Video) bundle format ──────────────
; Custom file association so .rcpdv files double-click into the
; editor while Windows still treats them as video for indexing,
; the file picker's "Videos" filter, and Photos integration. The
; bytes underneath are valid MP4 — any video tool that's pointed at
; the file directly can still play it. Spec: agents/skills/race-bundle-format/
;
; Per-user install (HKCU) matches PrivilegesRequired=lowest above.
Root: HKCU; Subkey: "Software\Classes\.rcpdv"; ValueType: string; ValueName: ""; ValueData: "ProDrive.Bundle"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.rcpdv"; ValueType: string; ValueName: "PerceivedType"; ValueData: "video"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.rcpdv"; ValueType: string; ValueName: "Content Type"; ValueData: "video/mp4"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\ProDrive.Bundle"; ValueType: string; ValueName: ""; ValueData: "RaceCor Pro Drive Bundle"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ProDrive.Bundle\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",1"
Root: HKCU; Subkey: "Software\Classes\ProDrive.Bundle\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; ── racecor-native:// custom URL protocol ─────────────────────
; OAuth redirect comes back to racecor-native://auth?code=… so
; Windows needs to know which app to hand the URI to. Without this,
; sign-in completes server-side but the browser hits "no app handles
; this protocol" and the user is stuck.
Root: HKCU; Subkey: "Software\Classes\racecor-native"; ValueType: string; ValueName: ""; ValueData: "URL:RaceCor Pro Drive Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\racecor-native"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\racecor-native\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",1"
Root: HKCU; Subkey: "Software\Classes\racecor-native\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; ── CHROME EXTENSION INSTALL (registry-based) ────────────────
; Chrome (and Chromium-based browsers) read external-extension
; entries under HKCU\Software\<browser>\Extensions\<extension-id>
; on launch. With `path` and `version` set, the browser locates the
; .crx and offers to install. Source: https://developer.chrome.com/
; docs/extensions/how-to/distribute/install-extensions
;
; Behaviour by browser as of mid-2024:
;   - Chrome stable: prompts "Add extension" on next launch IF the
;     user has Developer Mode enabled OR the extension is on the
;     Web Store. For pure consumer Chrome with developer mode off,
;     the extension is silently blocked — fall back to the host's
;     Load-unpacked card.
;   - Edge: same behaviour, separate registry hive.
;   - Brave / Opera / Vivaldi (Chromium): also read these keys.
;
; We register both Chrome and Edge so a single .crx ships to both
; user bases. Per-user (HKCU) install matches our PrivilegesRequired.
; Tasks: chromeextension gates these so the user can opt out from
; the Tasks page.
#ifdef EXTENSION_ID
#ifdef EXTENSION_VERSION
Root: HKCU; Subkey: "Software\Google\Chrome\Extensions\{#EXTENSION_ID}"; ValueType: string; ValueName: "path"; ValueData: "{app}\ChromeExtension\racecor-iracing-sync.crx"; Flags: uninsdeletekey; Tasks: chromeextension
Root: HKCU; Subkey: "Software\Google\Chrome\Extensions\{#EXTENSION_ID}"; ValueType: string; ValueName: "version"; ValueData: "{#EXTENSION_VERSION}"; Tasks: chromeextension
Root: HKCU; Subkey: "Software\Microsoft\Edge\Extensions\{#EXTENSION_ID}"; ValueType: string; ValueName: "path"; ValueData: "{app}\ChromeExtension\racecor-iracing-sync.crx"; Flags: uninsdeletekey; Tasks: chromeextension
Root: HKCU; Subkey: "Software\Microsoft\Edge\Extensions\{#EXTENSION_ID}"; ValueType: string; ValueName: "version"; ValueData: "{#EXTENSION_VERSION}"; Tasks: chromeextension
#endif
#endif

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings are intentionally NOT removed on uninstall — users almost
; always reinstall after a bad release and would be furious if their
; tuned HUD layout disappeared. They can delete the folder manually.
Type: filesandordirs; Name: "{app}"

[Code]
// ─── Previous-version auto-uninstall ──────────────────────────────────
// Inno Setup writes uninstall info under the AppId + "_is1" registry key.
// For per-user installs (PrivilegesRequired=lowest) it's HKCU; legacy
// installs may have landed in HKLM, so check both.
function GetUninstallString(): String;
var
  uninstKey: String;
  s: String;
begin
  // Must match the AppId GUID in [Setup] above. Inno Setup writes
  // uninstall info under "<AppId>_is1". (We don't use SetupSetting()
  // here because the {{ escaping interacts badly with Pascal comment
  // syntax in [Code] blocks.)
  uninstKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D6F2E0F3-7B6E-4C42-9FE6-3F4E4E5C0DBB}_is1';
  s := '';
  if not RegQueryStringValue(HKCU, uninstKey, 'UninstallString', s) then
    RegQueryStringValue(HKLM, uninstKey, 'UninstallString', s);
  Result := s;
end;

procedure UninstallPreviousVersion();
var
  uninstStr: String;
  resultCode: Integer;
begin
  uninstStr := GetUninstallString();
  if uninstStr = '' then Exit;
  uninstStr := RemoveQuotes(uninstStr);
  // /VERYSILENT suppresses all UI, /SUPPRESSMSGBOXES kills any leftover
  // confirmations, /NORESTART prevents reboot prompts. We don't care
  // about the exit code — even a partial uninstall makes room for a
  // clean install over the top.
  Exec(uninstStr, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '',
       SW_HIDE, ewWaitUntilTerminated, resultCode);
end;

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

// Recursively mirror every file from `srcDir` into `destDir`,
// preserving subdirectory structure. Used for the dataset folder
// only — SimHub's stock DLLs MUST NOT be overwritten so we don't
// use this for the root-level plugin DLLs (see CopyAllowListed).
procedure CopyTreeRecursive(srcDir, destDir: String);
var
  fr: TFindRec;
  childSrc, childDest: String;
begin
  if not DirExists(srcDir) then Exit;
  if not DirExists(destDir) then ForceDirectories(destDir);
  if FindFirst(srcDir + '\*', fr) then
  begin
    try
      repeat
        if (fr.Name = '.') or (fr.Name = '..') then Continue;
        childSrc := srcDir + '\' + fr.Name;
        childDest := destDir + '\' + fr.Name;
        if (fr.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          CopyTreeRecursive(childSrc, childDest)
        else
          FileCopy(childSrc, childDest, False);
      until not FindNext(fr);
    finally
      FindClose(fr);
    end;
  end;
end;

// Copy only the files SimHub doesn't ship. CRITICAL: do not bulk-copy
// the {app}\Plugin tree into SimHub. SimHub bundles its own
// Newtonsoft.Json, log4net, and the BCL polyfills (System.Buffers,
// System.Memory, System.Threading.Tasks.Extensions, etc.) and does
// strict patch-version checks on those at startup - any mismatch
// silently disables its ENTIRE plugin manager, not just our plugin.
// This list is intentionally narrow: our DLL + clearly-third-party
// deps SimHub doesn't ship. Mirrors the allow-list in
// scripts/windows/install.bat (dev tool); keep them in sync when
// adding new plugin dependencies.
procedure CopyAllowListed(srcDir, destDir: String);
var
  i: Integer;
  allowList: array of String;
  src: String;
begin
  if not DirExists(srcDir) then Exit;
  if not DirExists(destDir) then ForceDirectories(destDir);
  SetArrayLength(allowList, 6);
  allowList[0] := 'RaceCorProDrive.dll';
  allowList[1] := 'RaceCorProDrive.pdb';
  allowList[2] := 'IRSDKSharper.dll';
  allowList[3] := 'Fleck.dll';
  allowList[4] := 'MessagePack.dll';
  allowList[5] := 'MessagePack.Annotations.dll';
  for i := 0 to GetArrayLength(allowList) - 1 do
  begin
    src := srcDir + '\' + allowList[i];
    if FileExists(src) then
      FileCopy(src, destDir + '\' + allowList[i], False);
  end;
end;

// True if SimHubWPF.exe is currently running.
function IsSimHubRunning(): Boolean;
var
  resultCode: Integer;
begin
  // tasklist exits 0 even when the process isn't found; we check the
  // output via findstr. ShellExec with SW_HIDE keeps the user from
  // seeing a console flash.
  Exec(ExpandConstant('{cmd}'),
       '/C tasklist /FI "IMAGENAME eq SimHubWPF.exe" | findstr /I "SimHubWPF.exe"',
       '', SW_HIDE, ewWaitUntilTerminated, resultCode);
  Result := (resultCode = 0);
end;

// Politely ask SimHub to close, escalating to taskkill /F if needed.
// Returns True iff SimHub is no longer running when we exit.
function StopSimHub(): Boolean;
var
  resultCode: Integer;
  attempts: Integer;
begin
  if not IsSimHubRunning() then begin Result := True; Exit; end;
  // taskkill without /F first - lets SimHub flush state.
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /IM SimHubWPF.exe /T',
       '', SW_HIDE, ewWaitUntilTerminated, resultCode);
  for attempts := 1 to 10 do
  begin
    Sleep(500);
    if not IsSimHubRunning() then begin Result := True; Exit; end;
  end;
  // Force-kill fallback so the file copies don't fail with sharing
  // violations on RaceCorProDrive.dll.
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /F /IM SimHubWPF.exe /T',
       '', SW_HIDE, ewWaitUntilTerminated, resultCode);
  Sleep(1000);
  Result := not IsSimHubRunning();
end;

// Copy plugin DLLs + datasets from {app}\Plugin into the SimHub install
// dir so SimHub auto-loads RaceCorProDrive.dll on next start. Runs
// after the main file copy so {app}\Plugin already exists.
procedure InstallSimHubPlugin();
var
  simHub: String;
  pluginSrc: String;
  wasRunning: Boolean;
  resultCode: Integer;
begin
  pluginSrc := ExpandConstant('{app}\Plugin');
  if not DirExists(pluginSrc) then Exit;
  simHub := FindSimHubDir();
  if simHub = '' then
  begin
    MsgBox('SimHub was not detected on this machine.' + #13#10 + #13#10 +
           'The RaceCor SimHub plugin files are at:' + #13#10 + pluginSrc + #13#10 + #13#10 +
           'After installing SimHub, copy that folder''s contents into the SimHub ' +
           'install folder (next to SimHubWPF.exe) to enable the plugin.',
           mbInformation, MB_OK);
    Exit;
  end;

  // Stop SimHub before touching its install dir, so DLL replacement
  // doesn't fail with a file lock and the user doesn't end up with
  // a stale plugin loaded in the running instance.
  wasRunning := IsSimHubRunning();
  if wasRunning then
  begin
    if not StopSimHub() then
    begin
      MsgBox('Could not stop SimHub. Close it manually and re-run the installer ' +
             'to finish installing the RaceCor plugin.',
             mbError, MB_OK);
      Exit;
    end;
  end;

  // Copy ONLY the allow-listed DLLs (our DLL + clearly-third-party
  // deps SimHub doesn't ship). Bulk-copying the {app}\Plugin tree
  // here would overwrite SimHub's stock Newtonsoft.Json, log4net,
  // System.Buffers, System.Memory, etc. with our copies — SimHub's
  // strict patch-version checks then silently disable its entire
  // plugin manager (this is what was murdering SimHub installs).
  CopyAllowListed(pluginSrc, simHub);

  // Dataset folder is safe to bulk-mirror — SimHub never ships
  // racecorprodrive-data/ so there's nothing to overwrite. Recursive
  // copy preserves the subfolder structure (commentary fragments,
  // tracks, cars, etc.).
  if DirExists(pluginSrc + '\racecorprodrive-data') then
    CopyTreeRecursive(pluginSrc + '\racecorprodrive-data', simHub + '\racecorprodrive-data');

  // Restart SimHub if we shut it down. Best-effort - if the launch
  // fails, the next SimHub start will pick the plugin up anyway.
  if wasRunning then
  begin
    Exec(simHub + '\SimHubWPF.exe', '', simHub,
         SW_SHOWNORMAL, ewNoWait, resultCode);
  end;
end;

// ─── Stream Deck plugin install ──────────────────────────────────────
// Stream Deck reads plugins from %APPDATA%\Elgato\StreamDeck\Plugins\.
// The .sdPlugin ships as a real on-disk folder at
// {app}\StreamDeckPlugin\com.k10motorsports.racecor.overlay.sdPlugin
// (CI bundles it from the overlay's streamdeck-plugin.zip artifact —
// the in-asar copy under {app}\Overlay\... is invisible to file
// operations and was the reason 0.18.x silently no-op'd this step).
// Active install copies that folder into the Stream Deck plugins dir
// so the action keys appear the next time Stream Deck restarts. If
// Stream Deck isn't installed yet, leave the source bundled — the
// host has a "Install Stream Deck plugin" button that re-runs this
// copy.
procedure InstallStreamDeckPlugin();
var
  pluginsDir: String;
  srcDir: String;
  destDir: String;
begin
  srcDir := ExpandConstant('{app}\StreamDeckPlugin\com.k10motorsports.racecor.overlay.sdPlugin');
  if not DirExists(srcDir) then Exit;

  // Per-user Stream Deck plugins folder. Stream Deck only reads from
  // here — there's no machine-wide alternative. ExpandConstant
  // resolves to %APPDATA% (HKCU-equivalent) which matches our
  // PrivilegesRequired=lowest install posture.
  pluginsDir := ExpandConstant('{userappdata}\Elgato\StreamDeck\Plugins');
  if not DirExists(pluginsDir) then
  begin
    // Stream Deck not installed yet. Don't error — the .sdPlugin
    // stays bundled at srcDir and the host can drop it in later
    // via its "Install Stream Deck plugin" action.
    Exit;
  end;

  destDir := pluginsDir + '\com.k10motorsports.racecor.overlay.sdPlugin';
  // Mirror the whole tree, overwriting an older install in place.
  // Stream Deck reloads its plugin index on next launch.
  CopyTreeRecursive(srcDir, destDir);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Run the previous-version uninstaller right before our files land,
  // so the user doesn't have to manually uninstall between iterations.
  if CurStep = ssInstall then UninstallPreviousVersion();

  // Companion installs fire post-copy and only if their Tasks-page
  // checkbox is still ticked. Each guards itself against missing
  // payloads / target apps so an unselected task is a no-op.
  if CurStep = ssPostInstall then
  begin
    if IsTaskSelected('simhubplugin') then InstallSimHubPlugin();
    if IsTaskSelected('streamdeckplugin') then InstallStreamDeckPlugin();
  end;
end;
