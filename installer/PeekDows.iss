; ===========================================================================
; PeekDows — Inno Setup script (user-level install, no admin required)
;
; Prerequisites:
;   1. Build the portable single-file output first:
;        .\scripts\publish-windows.ps1
;      (produces artifacts\publish\win-x64\PeekDows.App.exe + Assets\)
;   2. Compile this script with Inno Setup 6:
;        ISCC.exe installer\PeekDows.iss
;      Output: artifacts\installer\PeekDowsSetup-x64.exe
;
; Start with Windows is enabled by the app itself: after copying the files,
; the installer launches "PeekDows.App.exe --enable-startup" which reuses the
; existing StartupService to create the Startup folder shortcut and flip the
; settings flag, keeping the tray menu state in sync.
; ===========================================================================

#define MyAppName        "PeekDows"
#define MyAppVersion     "0.1.0"
#define MyAppPublisher   "PeekDows"
#define MyAppExeName     "PeekDows.App.exe"
#define MyAppURL         "https://github.com/Abdoullah0055/PeekDows"

; Relative to this .iss file (installer\).
#define PublishDir       "..\artifacts\publish\win-x64"
#define TrayIcon         "..\src\PeekDows.App\Assets\peekdows_tray_icon_bw.ico"

[Setup]
AppId={{8F2C4A1E-3B5D-4E6F-9A7C-1D2E3F405160}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
OutputDir=..\artifacts\installer
OutputBaseFilename=PeekDowsSetup-x64
SetupIconFile={#TrayIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; The tray app has no main window, so let the explicit taskkill (see [Code])
; handle stopping it instead of Inno's file-handle scan + restart dance.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; Single-file self-contained build + Assets subfolder (tray icon, etc.).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Start Menu shortcut (always created — required by the task).
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "PeekDows — window arrangement"
; Uninstall entry in Start Menu.
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; Comment: "Remove {#MyAppName}"
; Optional desktop shortcut.
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "PeekDows — window arrangement"

[Run]
; Two separate steps so the tray app actually starts after install:
;   1. Hook "PeekDows.App.exe --enable-startup" — activates Start with Windows
;      via the existing StartupService, then EXITS immediately (it does not run
;      the tray app). runhidden + waituntilterminated so it finishes BEFORE step 2.
;   2. Real launch with NO arguments — runs the tray app normally so the icon
;      appears and stays in the background. The postinstall checkbox on GUI
;      installs lets the user opt out (default checked).

; --- GUI install ---
; Step 1 (silent hook, must complete first)
Filename: "{app}\{#MyAppExeName}"; Parameters: "--enable-startup"; Flags: runhidden waituntilterminated; Check: WizardSilentLaunchAllowed
; Step 2 (real launch, user can uncheck via postinstall)
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent; Check: WizardSilentLaunchAllowed

; --- Silent install ---
; Step 1 (silent hook)
Filename: "{app}\{#MyAppExeName}"; Parameters: "--enable-startup"; Flags: runhidden waituntilterminated; Check: WizardIsSilent
; Step 2 (real launch, no checkbox in silent mode)
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardIsSilent

[UninstallRun]
; Best-effort: ask the running app to exit via taskkill before removing files.
; (InitializeUninstall also kills it; this is a backstop.)
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; Remove the Startup shortcut regardless of who created it (installer or app),
; so uninstall never leaves a dead shortcut behind.
Type: files; Name: "{userstartup}\PeekDows.lnk"

[Code]
function WizardIsSilent: Boolean;
begin
  Result := WizardSilent();
end;

// postinstall entries only run on GUI installs; gate the GUI launch here.
function WizardSilentLaunchAllowed: Boolean;
begin
  Result := not WizardSilent();
end;

procedure KillRunningApp;
var
  ResultCode: Integer;
begin
  // Silently ignore the exit code: a non-zero result just means the app was
  // not running, which is fine.
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM {#MyAppExeName}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  KillRunningApp;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  KillRunningApp;
  Result := True;
end;
