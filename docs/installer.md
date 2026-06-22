# PeekDows — Windows Installer

This document explains how to produce a distributable Windows installer for
PeekDows and what it does once installed.

## Overview

PeekDows ships as a **self-contained, single-file** .NET 10 build, wrapped in an
**Inno Setup** installer. The installer:

- Installs PeekDows as a normal Windows app (user-level, **no admin required**).
- Creates a **Start Menu** shortcut.
- Optionally creates a **Desktop** shortcut.
- **Launches PeekDows** after installation.
- **Enables "Start with Windows" by default** (Startup folder shortcut).
- Registers a clean **Uninstall** entry.

## Prerequisites

| Tool | Required for | How to get it |
|---|---|---|
| .NET 10 SDK | Building the app / portable build | https://dotnet.microsoft.com/download |
| Inno Setup 6 | Building the installer (`.exe`) | https://jrsoftware.org/isdl.php |

> The portable build step needs only the .NET SDK. Inno Setup is only needed to
> wrap that build into the setup `.exe`.

## Build the portable single-file output

From the repository root:

```powershell
.\scripts\publish-windows.ps1
```

This runs:

```powershell
dotnet publish src\PeekDows.App\PeekDows.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o artifacts\publish\win-x64
```

Output: `artifacts\publish\win-x64\PeekDows.App.exe` (+ `Assets\` with the tray
icon). This is a **standalone build** — no .NET runtime is required on the
target machine.

## Build the installer

If Inno Setup is installed and `ISCC.exe` is on your `PATH`:

```powershell
ISCC.exe installer\PeekDows.iss
```

If `ISCC.exe` is not on your `PATH`, use the full path, e.g.:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\PeekDows.iss
```

Output: **`artifacts\installer\PeekDowsSetup-x64.exe`**

This is the file you copy/download to the target PC and double-click to install.

## What gets installed

| Item | Location |
|---|---|
| App files | `%LOCALAPPDATA%\Programs\PeekDows\` |
| Start Menu shortcut | Start Menu → PeekDows → PeekDows |
| Uninstall shortcut | Start Menu → PeekDows → Uninstall PeekDows |
| Desktop shortcut (optional) | `%USERPROFILE%\Desktop\PeekDows.lnk` |
| Startup shortcut | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\PeekDows.lnk` |
| Settings | `%APPDATA%\PeekDows\settings.json` |
| Logs | `%APPDATA%\PeekDows\logs\peekdows.log` |

> Settings and logs live under `%APPDATA%` and are **not** removed on uninstall
> (they are user data). Remove them manually if you want a fully clean slate.

## "Start with Windows" behavior

The installer does **not** create the Startup shortcut directly. Instead it
launches `PeekDows.App.exe --enable-startup`, which reuses the app's existing
`StartupService` to:

1. Create `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\PeekDows.lnk`
   pointing at the **installed** executable (`Environment.ProcessPath`).
2. Flip the `StartWithWindows` setting to `true` so the tray menu checkmark and
   the shortcut stay in sync.

The shortcut's `TargetPath` always points to the installed exe (never the dev
build), and its icon is taken from the installed exe (`IconLocation = "<exe>,0"`).

- If the user later **disables** "Start with Windows" from the tray, the
  `StartupService` deletes the shortcut — same one the installer enabled.
- If the user **re-enables** it, the shortcut is recreated pointing at the
  installed exe.
- On **uninstall**, the Inno script also removes `PeekDows.lnk` from the Startup
  folder, so no dead shortcut is ever left behind.

## Manual test checklist

After generating `PeekDowsSetup-x64.exe`:

1. Run the installer on the target PC.
2. Confirm PeekDows launches after install and the **tray icon** appears.
3. Confirm `Ctrl+Alt+Space` triggers an arrange.
4. Confirm `Ctrl+Shift` + mouse movement (Directional Focus) still works.
5. Confirm the Startup shortcut points at the installed exe (right-click →
   Properties → Target).
6. **Reboot Windows** → confirm PeekDows starts automatically.
7. Disable "Start with Windows" from the tray → confirm the Startup shortcut is
   removed.
8. Re-enable it → confirm the shortcut is recreated.
9. Uninstall PeekDows (Settings → Apps, or the Start Menu shortcut) → confirm
   app files and all shortcuts (Start Menu, Desktop, Startup) are removed.

## Limitations

- **x64 only** (the RID is `win-x64`). The installer also allows installation on
  ARM64 Windows via `x64compatible` (x64 emulation), but there is no native
  ARM64 build.
- The installer is **not code-signed**, so Windows SmartScreen may show a warning
  on first run. Click **More info → Run anyway**.
- The .NET version is baked into the single-file build; upgrading .NET requires
  rebuilding the publish output and the installer.
