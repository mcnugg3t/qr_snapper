; QR Snapper installer script for Inno Setup 6.
;
; Per-user install to %LOCALAPPDATA%\Programs\QRSnapper\. No admin/UAC needed.
; Auto-start is owned by the in-app setting (writes HKCU\Run from the app's
; Settings window) so the installer doesn't touch it — keeps the source of
; truth in one place.
;
; To compile: build-installer.ps1 in the parent folder runs dotnet publish
; first, then invokes ISCC.exe on this file. Output goes to installer\Output\.

#define MyAppName "QR Snapper"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Caleb Stevens"
#define MyAppExeName "QRSnapper.exe"
#define MyAppDescription "Snip a screen region and decode any QR codes inside to your clipboard."

; Path to the dotnet publish output. build-installer.ps1 ensures this exists.
#define MyPublishDir "..\src\QrSnip\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; A stable AppId is what Windows uses to detect "this app is already installed,
; we're upgrading rather than installing fresh." Generate once, never change.
; Generated as a GUID literal so it survives if MyAppName ever changes again.
AppId={{8C2F5A41-3E1D-4C7B-9F86-72A1B4D7C2E9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (c) 2026 {#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppDescription}

; Per-user install:
;   - PrivilegesRequired=lowest = no UAC prompt.
;   - DefaultDirName under {localappdata} = no admin needed to write here.
;   - DisableDirPage=yes = don't show the install location page (we're picking
;     a sensible default; the user doesn't need to choose).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={localappdata}\Programs\QRSnapper
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}

; Output settings.
OutputDir=Output
OutputBaseFilename=QRSnapperSetup
SetupIconFile=..\src\QrSnip\Assets\qr_snapper_icon.ico
Compression=lzma2/max
SolidCompression=yes

; Visual polish.
WizardStyle=modern
ShowLanguageDialog=no
DisableWelcomePage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; Require Windows 10 1809+ for WGC + Win2D.
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Single optional task: create a desktop shortcut. Off by default to keep
; install minimal; user can opt in via the wizard checkbox.
; We intentionally do NOT offer "start with Windows" here — the app's own
; Settings window owns that toggle (writes HKCU\Run), and offering it in two
; places would invite the two to drift out of sync.
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Recursively copy the entire publish output. *.pdb and *.xml are debugging
; symbols / IntelliSense docs that aren't needed at runtime.
;
; Note: build-installer.ps1 prunes empty locale folders and WindowsAppSDK
; .mui files before invoking ISCC, so we don't need an exclusion pattern
; here -- doing the pruning in PowerShell is more precise than Inno's
; glob system (which is case-insensitive and tricky with multi-segment
; locale names like "az-Latn-AZ").
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
; Start menu shortcut (always created).
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"
; Uninstall entry under the Start menu group, so users can find it next to the app.
Name: "{userprograms}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Optional desktop shortcut (only if user checked the task).
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; No [Run] section: we deliberately don't offer "Launch QR Snapper now" at
; the end of the installer. Reason: launching immediately after install can
; race the Windows shell finishing its tray-cache refresh, causing the tray
; icon registration to fail with "TryCreate failed". We have retry logic
; in TrayIcon.ForceCreateWithRetry that papers over most cases, but the
; cleanest fix is to just let the user launch from the Start menu themselves
; after install completes. By then the shell has settled.

[UninstallRun]
; On uninstall, ALSO remove our HKCU\Run auto-start entry if the user had
; enabled it. We use reg.exe to delete the value if it exists; the /f flag
; suppresses the confirm prompt, and we ignore the exit code so uninstall
; succeeds whether or not the key was present.
Filename: "{cmd}"; Parameters: "/c reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""QR Snapper"" /f"; Flags: runhidden; RunOnceId: "RemoveAutoStart"

[Code]
// Post-install message: tell the user about the AV-flagging possibility.
// Better to surface this honestly than have them silently confused when
// their AV quarantines the app a week later.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    MsgBox(
      'QR Snapper is installed!' + #13#10#13#10 +
      'Heads up: because QR Snapper registers a global hotkey, reads from the screen, ' +
      'and writes to the clipboard, some antivirus programs may flag it as suspicious. ' +
      'It is none of those things, but the behavior pattern is similar to a keylogger.' + #13#10#13#10 +
      'If your antivirus quarantines QR Snapper, please:' + #13#10 +
      '  1. Restore it from your antivirus quarantine.' + #13#10 +
      '  2. Add an exception for the QR Snapper install folder.' + #13#10#13#10 +
      'If you''re not sure how, ask Caleb or your IT department for help.',
      mbInformation,
      MB_OK);
  end;
end;
