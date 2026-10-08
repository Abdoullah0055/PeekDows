using System;
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Bridge hotkeys contract: snapshot exposes arrangeNow/pauseResume defaults,
/// unchanged values are no-ops, a conflicting gesture yields applied{ok:false}.
/// Helpers are nested (local to this file) to avoid cross-file collisions.
/// </summary>
public sealed class SettingsBridgeHotkeysTests
{
    private sealed class TempHotkeyScope : IDisposable
    {
        public string TempDir { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        public SettingsService Service { get; }
        public AppSettings Settings { get; }
        public HotkeyFakeController Controller { get; }
        public SettingsBridge Bridge { get; }

        public TempHotkeyScope()
        {
            System.IO.Directory.CreateDirectory(TempDir);
            Service = new SettingsService(System.IO.Path.Combine(TempDir, "settings.json"));
            Settings = Service.Load();
            Controller = new HotkeyFakeController(Settings);
            Bridge = new SettingsBridge(Controller, Service, appVersionProvider: () => "9.9.9");
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(TempDir)) System.IO.Directory.Delete(TempDir, true);
        }
    }

    private sealed class HotkeyFakeController : IPeekDowsController
    {
        public HotkeyFakeController(AppSettings settings) => CurrentSettings = settings;

        public int TryUpdateCallCount { get; private set; }
        public AppSettings CurrentSettings { get; }
        public RuntimeState State => RuntimeState.Running;
        public string LogFilePath => "";
        public bool IsAutoArrangeRunning => false;
        public bool IsStartWithWindowsEnabled => CurrentSettings.StartWithWindows;
        public bool IsDirectionalFocusEnabled => CurrentSettings.DirectionalFocusEnabled;
        public bool IsAnimateWindowTransitionsEnabled => CurrentSettings.AnimateWindowTransitions;
        public bool AllowRepositionMaximizedWindows => CurrentSettings.AllowRepositionMaximizedWindows;
        public WindowSizePreset CurrentWindowSizePreset => CurrentSettings.WindowSizePreset;
        public bool IsPaused => false;
        public DateTimeOffset? PauseUntil => null;
        public string? PauseDescription => null;

        public event Action<RuntimeState>? StateChanged;
        public event Action<bool>? AutoArrangeChanged;
        public event Action<bool>? StartWithWindowsChanged;
        public event Action<bool>? DirectionalFocusChanged;
        public event Action<bool>? AnimateWindowTransitionsChanged;
        public event Action<bool>? AllowRepositionMaximizedWindowsChanged;
        public event Action<WindowSizePreset>? WindowSizePresetChanged;

        public bool TryUpdateHotkey(string name, string gesture, out string error)
        {
            TryUpdateCallCount++;
            if (string.Equals(gesture, "Ctrl+Alt+Conflict", StringComparison.OrdinalIgnoreCase))
            {
                error = "hotkey-conflict";
                return false;
            }
            CurrentSettings.Hotkeys[name] = gesture;
            error = "";
            return true;
        }

        public void ArrangeNow() { }
        public void TogglePause() { }
        public void PauseFor(TimeSpan duration) { }
        public void PauseUntilResumed() { }
        public void Resume() { }
        public void ToggleAutoArrange() { }
        public void ToggleStartWithWindows() { }
        public void ToggleDirectionalFocus() { }
        public void ToggleAnimateWindowTransitions() { }
        public void ToggleAllowRepositionMaximizedWindows() { }
        public void SetWindowSizePreset(WindowSizePreset preset) { }
        public void OpenSettings() { }
        public void OpenLogFile() { }
        public void OpenLogsFolder() { }
        public void OnSettingsChanged() { }
        public void Exit() { }
    }

    [Fact]
    public void Snapshot_ContainsHotkeysDefaults()
    {
        using var tmp = new TempHotkeyScope();
        using var doc = JsonDocument.Parse(tmp.Bridge.BuildSnapshotMessage());
        var hotkeys = doc.RootElement.GetProperty("data").GetProperty("hotkeys");
        Assert.Equal("Ctrl+Alt+Space", hotkeys.GetProperty("arrangeNow").GetString());
        Assert.Equal("Ctrl+Alt+P", hotkeys.GetProperty("pauseResume").GetString());
    }

    [Fact]
    public void Apply_SameValue_DoesNothing()
    {
        using var tmp = new TempHotkeyScope();
        var cur = tmp.Settings.Hotkeys["arrangeNow"];
        var resp = tmp.Bridge.HandleMessage(
            "{\"type\":\"apply\",\"data\":{\"hotkeys\":{\"arrangeNow\":\"" + cur + "\"}}}");
        Assert.Equal(0, tmp.Controller.TryUpdateCallCount);
        using var doc = JsonDocument.Parse(resp!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Apply_Conflict_ReturnsHotkeyConflict_AndKeepsOld()
    {
        using var tmp = new TempHotkeyScope();
        var before = tmp.Settings.Hotkeys["arrangeNow"];
        var resp = tmp.Bridge.HandleMessage(
            """{"type":"apply","data":{"hotkeys":{"arrangeNow":"Ctrl+Alt+Conflict"}}}""");
        using var doc = JsonDocument.Parse(resp!);
        var root = doc.RootElement;
        Assert.Equal("applied", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("hotkey-conflict", root.GetProperty("error").GetString());
        Assert.Equal(before, tmp.Settings.Hotkeys["arrangeNow"]);
    }
}
