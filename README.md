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
| `Hotkeys` | dict | `{}` | Custom hotkey mappings (arrangeNow/pauseResume editable via Settings → Hotkeys) |

> **Note on `Hotkeys` / `OverflowBehavior`**: Hotkeys arrangeNow/pauseResume are editable from Settings → Hotkeys; `OverflowBehavior` is deprecated and has no effect — both are kept for backward compatibility.

Open Settings from the tray menu or by double-clicking the tray icon. In the modern WebView2 window, preference changes apply when you press Save. **General** also shows the current status and lets you pause arranging for a set duration. **Beta** contains focus hints and arrange sounds, and **About** opens the logs. Without WebView2, the basic settings window provides these commands and the ignore lists.

A monitor with exactly **one** arrangeable window gets that window **natively maximized** (instead of a 90% slot). If you manually restore a window PeekDows had maximized, it is left alone until a window opens or closes on that monitor.


## Layout tools

In **Settings → Layout**:

- **Preview arrange** shows the proposed zones and windows left in place. Press Enter to apply the preview or Escape to close it. **Arrange Now** in the tray and its shortcut still apply immediately.
- **Edit profiles** stores optional display-specific modes and app rules. Displays without a profile use the default layout and rules. **Custom** supports an adjustable left/right split and an optional split on the right. PeekDows can move a window that cannot fit to the full work area and shows a notification when it does.
- **Next window in zone** cycles windows stacked by Halves and Thirds after a successful arrange. If several stacks are available, choose which one to cycle.
- **Layout settings file → Export / Import** transfers modes, zones, display profiles, app rules and shortcuts in versioned JSON. Import shows the proposed changes and backs up the current settings before applying them.

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

MIT — see [LICENSE](LICENSE).

This project depends on the Microsoft Edge WebView2 runtime for its
settings window. Building from source restores the `Microsoft.Web.WebView2`
NuGet package, which is distributed under Microsoft's own WebView2 terms
(see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md)).
