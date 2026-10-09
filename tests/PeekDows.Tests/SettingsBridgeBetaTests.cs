using System;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Agent C (Beta page): snapshot exposes focusHintMode only (default "Overlay"),
/// apply persists Off|Overlay (case-insensitive) and ignores anything else.
/// Legacy values (Both, Spotlight) are invalid bridge-side and ignored — the V5
/// Core migration normalizes them at load.
/// </summary>
public sealed class SettingsBridgeBetaTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly FakeBetaController _controller;
    private readonly SettingsBridge _bridge;

    public SettingsBridgeBetaTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(System.IO.Path.Combine(_tempDir, "settings.json"));
        _settings = _settingsService.Load();
        _controller = new FakeBetaController(_settings, _settingsService);
        _bridge = new SettingsBridge(_controller, _settingsService, appVersionProvider: () => "9.9.9");
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Snapshot_NullHintMode_FallsBackToOverlay()
    {
        _settings.FocusHintMode = null!;

        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("Overlay", data.GetProperty("focusHintMode").GetString());
    }

    [Fact]
    public void Snapshot_ReflectsMutatedHintMode()
    {
        _settings.FocusHintMode = "Off";

        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("Off", data.GetProperty("focusHintMode").GetString());
    }

    [Fact]
    public void Snapshot_OmitsRemovedBetaKeys()
    {
        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.True(data.TryGetProperty("focusHintMode", out _));
        Assert.False(data.TryGetProperty("overlayShowIcons", out _));
        Assert.False(data.TryGetProperty("spotlightOpacity", out _));
    }

    [Fact]
    public void Apply_OverlayToOff_PersistsNotifiesAndSnapshots()
    {
        _settings.FocusHintMode = "Overlay";

        var response = _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Off"}}""");

        Assert.Equal("Off", _settings.FocusHintMode);
        Assert.Equal("Off", _settingsService.Load().FocusHintMode);
        Assert.Equal(1, _controller.OnSettingsChangedCount);

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Off", doc.RootElement.GetProperty("data").GetProperty("focusHintMode").GetString());
    }

    [Fact]
    public void Apply_OffToOverlay_Persists()
    {
        _settings.FocusHintMode = "Off";

        _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Overlay"}}""");

        Assert.Equal("Overlay", _settings.FocusHintMode);
        Assert.Equal("Overlay", _settingsService.Load().FocusHintMode);
        Assert.Equal(1, _controller.OnSettingsChangedCount);
    }

    [Theory]
    [InlineData("off", "Off")]
    [InlineData("oFf", "Off")]
    [InlineData("overlay", "Overlay")]
    [InlineData("OVERLAY", "Overlay")]
    public void Apply_HintMode_IsCaseInsensitive(string input, string expected)
    {
        _settings.FocusHintMode = expected == "Off" ? "Overlay" : "Off";

        _bridge.HandleMessage("{\"type\":\"apply\",\"data\":{\"focusHintMode\":\"" + input + "\"}}");

        Assert.Equal(expected, _settings.FocusHintMode);
    }

    [Fact]
    public void Apply_InvalidHintMode_Ignored()
    {
        _settings.FocusHintMode = "Overlay";

        var response = _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Laser"}}""");

        Assert.Equal("Overlay", _settings.FocusHintMode);
        Assert.Equal(0, _controller.OnSettingsChangedCount);
        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("Both")]
    [InlineData("Spotlight")]
    [InlineData("both")]
    [InlineData("spotlight")]
    public void Apply_LegacyHintMode_IgnoredByBridge(string legacy)
    {
        // The bridge only normalizes Off|Overlay. Legacy values (Both, Spotlight)
        // are invalid here and ignored; the V5 Core migration normalizes them
        // at load instead.
        _settings.FocusHintMode = "Overlay";

        _bridge.HandleMessage("{\"type\":\"apply\",\"data\":{\"focusHintMode\":\"" + legacy + "\"}}");

        Assert.Equal("Overlay", _settings.FocusHintMode);
        Assert.Equal(0, _controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_SameHintMode_DoesNotNotify()
    {
        _settings.FocusHintMode = "Overlay";

        _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Overlay"}}""");

        Assert.Equal(0, _controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_RemovedBetaKeys_AreIgnored()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"overlayShowIcons":false,"spotlightOpacity":60}}""");

        Assert.Equal(0, _controller.OnSettingsChangedCount);
    }

    /// <summary>
    /// Minimal fake controller mirroring real semantics. Implements the full
    /// IPeekDowsController surface (incl. SetFocusHintMode).
    /// </summary>
    private sealed class FakeBetaController : IPeekDowsController
    {
        private readonly SettingsService _settingsService;

        public FakeBetaController(AppSettings settings, SettingsService settingsService)
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

        public void SetFocusHintMode(string mode)
        {
            CurrentSettings.FocusHintMode = mode;
            _settingsService.Save(CurrentSettings);
        }
    }
}
