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

## Configuration

Settings are stored at `%APPDATA%\PeekDows\settings.json`.

| Setting | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | true | Master enable switch |
| `AutoArrange` | bool | false | Arrange continuously on timer |
| `ArrangeOnStartup` | bool | false | Arrange once on launch |
| `StartWithWindows` | bool | false | Create Startup folder shortcut |
| `SingleWindowMode` | bool | false | Single window per slot |
| `IgnoredProcesses` | string[] | built-in list | Process names to skip |
| `IgnoredClasses` | string[] | built-in list | Window class names to skip |
| `Hotkeys` | dict | `{}` | Custom hotkey mappings |

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
