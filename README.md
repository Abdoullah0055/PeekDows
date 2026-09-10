# PeekDows

Lightweight Windows utility that keeps your desktop tidy. It detects open windows and arranges them into a clean overlapping grid layout — automatically or on demand.

## Features

- **Arrange Now** — instantly tidy your desktop via tray menu or `Ctrl+Alt+Space`
- **Auto Arrange** — continuously monitors windows and arranges them as they open
- **ClassicPeekGrid Layout** — stacks windows at 90% of monitor work area, giving a peek at each
- **Multi-Monitor** — each monitor is arranged independently
- **Pause / Resume** — pause arrangement temporarily (5 min, 15 min, 1 hour, or until resumed)
- **Ignored Windows** — built-in system window filtering + user-customizable ignore lists
- **Start with Windows** — optional auto-start via Startup folder shortcut
- **Hotkeys** — `Ctrl+Alt+Space` (arrange), `Ctrl+Alt+P` (pause/resume)

## Requirements

- Windows 10/11
- .NET 10.0 SDK

## Build

```sh
dotnet build PeekDows.slnx
```

## Run

```sh
dotnet run --project src/PeekDows.App
```

## Test

```sh
dotnet test PeekDows.slnx
```

## Publish (Self-Contained)

```sh
dotnet publish src/PeekDows.App/PeekDows.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Output: `publish/PeekDows.App.exe` (standalone, no .NET runtime required on target machine).

## Windows Installer

A ready-to-run installer is produced from the self-contained build via [Inno Setup](https://jrsoftware.org/isdl.php).

**1. Build the portable single-file output:**

```powershell
.\scripts\publish-windows.ps1
```

Output: `artifacts\publish\win-x64\PeekDows.App.exe`

**2. Build the installer** (requires Inno Setup 6):

```powershell
ISCC.exe installer\PeekDows.iss
```

Output: `artifacts\installer\PeekDowsSetup-x64.exe` — copy this to the target PC and double-click to install.

The installer is user-level (no admin required), creates Start Menu + optional Desktop shortcuts, launches PeekDows after install, and **enables Start with Windows by default**. See [`docs/installer.md`](docs/installer.md) for full details.

## Configuration

Settings are stored at `%APPDATA%\PeekDows\settings.json`.

| Setting | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | true | Master enable switch |
| `AutoArrange` | bool | false | Arrange continuously on timer |
| `ArrangeOnStartup` | bool | false | Arrange once on launch |
| `StartWithWindows` | bool | false | Create Startup folder shortcut |
| `IgnoredProcesses` | string[] | built-in list | Process names to skip |
| `IgnoredClasses` | string[] | built-in list | Window class names to skip |
| `AnimateWindowTransitions` | bool | true | Animate window moves (150ms) with tween-then-snap maximize |
| `Hotkeys` | dict | `{}` | Custom hotkey mappings (configurable, not yet editable via UI) |

> **Note on `Hotkeys` / `OverflowBehavior`**: `Hotkeys` is persisted but not yet editable via the settings UI; `OverflowBehavior` is deprecated and has no effect — both are kept for backward compatibility.

A monitor with exactly **one** arrangeable window gets that window **natively maximized** (instead of a 90% slot). If you manually restore a window PeekDows had maximized, it is left alone until a window opens or closes on that monitor.


## Logs

Log file: `%APPDATA%\PeekDows\logs\peekdows.log`

## Project Structure

```
src/
  PeekDows.App/          — WinForms application (tray icon, hotkeys, UI)
  PeekDows.Core/          — Core logic (layout, classification, settings, Win32 P/Invoke)
tests/
  PeekDows.Tests/         — xunit test suite
```

## License

All rights reserved.
