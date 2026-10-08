using System;
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public sealed class SettingsBridgeIgnoredTests
{
    [Fact]
    public void Snapshot_ContainsIgnoredArrays()
    {
        using var tmp = new TempSettingsScope();
        var bridge = new SettingsBridge(tmp.Controller, tmp.Service, appVersionProvider: () => "9.9.9");
        using var doc = JsonDocument.Parse(bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.GetProperty("ignoredProcesses").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("ignoredClasses").ValueKind);
    }

    [Fact]
    public void Apply_IgnoredLists_DedupsPersistsAndNotifies()
    {
        using var tmp = new TempSettingsScope();
        var bridge = new SettingsBridge(tmp.Controller, tmp.Service, appVersionProvider: () => "9.9.9");
        var resp = bridge.HandleMessage("""{"type":"apply","data":{"ignoredProcesses":["a.exe","","A.EXE "],"ignoredClasses":["Foo"]}}""");
        Assert.Single(tmp.Settings.IgnoredProcesses);
        Assert.Equal("a.exe", tmp.Settings.IgnoredProcesses[0]);
        Assert.Equal(1, tmp.Controller.OnSettingsChangedCount);
        using var doc = JsonDocument.Parse(resp!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    private sealed class TempSettingsScope : IDisposable
    {
        private readonly string _tempDir;

        public TempSettingsScope()
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(_tempDir);
            Service = new SettingsService(System.IO.Path.Combine(_tempDir, "settings.json"));
            Settings = Service.Load();
            Controller = new FakeBridgeController(Settings, Service);
        }

        public SettingsService Service { get; }
        public AppSettings Settings { get; }
        public FakeBridgeController Controller { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Minimal fake controller mirroring real toggle semantics: mutate in place,
    /// persist via SettingsService, raise the Changed event.
    /// </summary>
    private sealed class FakeBridgeController : IPeekDowsController
    {
        private readonly SettingsService _settingsService;

        public FakeBridgeController(AppSettings settings, SettingsService settingsService)
        {
            CurrentSettings = settings;
            _settingsService = settingsService;
        }

        public int OnSettingsChangedCount { get; private set; }

        public RuntimeState State => RuntimeState.Running;
        public string LogFilePath => "";
        public AppSettings CurrentSettings { get; }
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

        public void ArrangeNow() { }
        public void TogglePause() { }
        public void PauseFor(TimeSpan duration) { }
        public void PauseUntilResumed() { }
        public void Resume() { }
        public void OpenSettings() { }
        public void OpenLogFile() { }
        public void OpenLogsFolder() { }
        public void Exit() { }
        public void OnSettingsChanged() { OnSettingsChangedCount++; }

        public void ToggleAutoArrange()
        {
            CurrentSettings.AutoArrange = !CurrentSettings.AutoArrange;
            _settingsService.Save(CurrentSettings);
            AutoArrangeChanged?.Invoke(CurrentSettings.AutoArrange);
        }

        public void ToggleStartWithWindows()
        {
            CurrentSettings.StartWithWindows = !CurrentSettings.StartWithWindows;
            _settingsService.Save(CurrentSettings);
            StartWithWindowsChanged?.Invoke(CurrentSettings.StartWithWindows);
        }

        public void ToggleDirectionalFocus()
        {
            CurrentSettings.DirectionalFocusEnabled = !CurrentSettings.DirectionalFocusEnabled;
            _settingsService.Save(CurrentSettings);
            DirectionalFocusChanged?.Invoke(CurrentSettings.DirectionalFocusEnabled);
        }

        public void ToggleAnimateWindowTransitions()
        {
            CurrentSettings.AnimateWindowTransitions = !CurrentSettings.AnimateWindowTransitions;
            _settingsService.Save(CurrentSettings);
            AnimateWindowTransitionsChanged?.Invoke(CurrentSettings.AnimateWindowTransitions);
        }

        public void ToggleAllowRepositionMaximizedWindows()
        {
            CurrentSettings.AllowRepositionMaximizedWindows = !CurrentSettings.AllowRepositionMaximizedWindows;
            _settingsService.Save(CurrentSettings);
            AllowRepositionMaximizedWindowsChanged?.Invoke(CurrentSettings.AllowRepositionMaximizedWindows);
        }

        public void SetWindowSizePreset(WindowSizePreset preset)
        {
            CurrentSettings.WindowSizePreset = preset;
            _settingsService.Save(CurrentSettings);
            WindowSizePresetChanged?.Invoke(preset);
        }
    }
}
