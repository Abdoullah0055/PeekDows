using System;
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public sealed class SettingsBridgeWindowsTests
{
    [Fact]
    public void TryHandle_RequestWindows_ReturnsRows()
    {
        var rows = new List<WindowRow> { new("123", "Notepad", "notepad.exe", "Notepad", true, "") };
        using var doc = JsonDocument.Parse("""{"type":"requestWindows"}""");
        var json = WindowsMessageHandler.TryHandle("requestWindows", doc.RootElement, () => rows, WindowsMessageHandler.CamelCase);
        Assert.NotNull(json);
        using var outDoc = JsonDocument.Parse(json!);
        Assert.Equal("windows", outDoc.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, outDoc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public void TryHandle_UnknownType_ReturnsNull()
    {
        var rows = new List<WindowRow> { new("123", "Notepad", "notepad.exe", "Notepad", true, "") };
        using var doc = JsonDocument.Parse("""{"type":"bogus"}""");
        var json = WindowsMessageHandler.TryHandle("bogus", doc.RootElement, () => rows, WindowsMessageHandler.CamelCase);
        Assert.Null(json);
    }

    [Fact]
    public void Bridge_RequestWindows_ReturnsRows()
    {
        using var tmp = new TempSettingsScope();
        var rows = new List<WindowRow> { new("123", "Notepad", "notepad.exe", "Notepad", true, "") };
        var bridge = new SettingsBridge(tmp.Controller, tmp.Service, appVersionProvider: () => "9.9.9");
        bridge.SetWindowsProvider(() => rows);
        bridge.SetWindowsMessageHandler((t, r) => WindowsMessageHandler.TryHandle(t, r, () => rows, WindowsMessageHandler.CamelCase));
        using var doc = JsonDocument.Parse(bridge.HandleMessage("""{"type":"requestWindows"}""")!);
        Assert.Equal("windows", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    /// <summary>Local temp scope: isolated SettingsService + minimal fake controller.</summary>
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

    /// <summary>Minimal fake controller mirroring toggle semantics (no-op toggles).</summary>
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
        public event Action<string>? FocusHintModeChanged;

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
        public void SetFocusHintMode(string mode) { CurrentSettings.FocusHintMode = mode; }

        public void SetWindowSizePreset(WindowSizePreset preset) { }
        public void OpenSettings() { }
        public void OpenLogFile() { }
        public void OpenLogsFolder() { }
        public void OnSettingsChanged() { OnSettingsChangedCount++; }
        public void Exit() { }
    }
}
