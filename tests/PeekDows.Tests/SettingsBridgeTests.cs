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
/// Covers the pure JS⇄C# settings bridge. The real PeekDowsAppContext is wired to live
/// Win32 services, so the bridge contract is tested through a fake controller that
/// mirrors the real toggle semantics (mutate in place, persist, raise the *Changed event).
/// </summary>
public class SettingsBridgeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly FakeBridgeController _controller;
    private readonly SettingsBridge _bridge;

    public SettingsBridgeTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(System.IO.Path.Combine(_tempDir, "settings.json"));
        _settings = _settingsService.Load();
        _controller = new FakeBridgeController(_settings, _settingsService);
        _bridge = new SettingsBridge(_controller, _settingsService, appVersionProvider: () => "9.9.9");
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void BuildSnapshotMessage_HasTypeSettings_AndCamelCaseFields()
    {
        var json = _bridge.BuildSnapshotMessage();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("settings", root.GetProperty("type").GetString());

        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("enabled").GetBoolean());
        Assert.False(data.GetProperty("autoArrange").GetBoolean());
        Assert.True(data.GetProperty("animate").GetBoolean());
        Assert.True(data.GetProperty("directionalFocus").GetBoolean());
        Assert.False(data.GetProperty("startWithWindows").GetBoolean());
        Assert.False(data.GetProperty("allowRepositionMaximized").GetBoolean());
        Assert.Equal("Small", data.GetProperty("preset").GetString());
        Assert.False(data.GetProperty("arrangeOnStartup").GetBoolean());
        Assert.False(data.GetProperty("showTrayNotifications").GetBoolean());
        Assert.Equal(50, data.GetProperty("thresholdPx").GetInt32());
        Assert.Equal(3, data.GetProperty("version").GetInt32());
        Assert.Equal("9.9.9", data.GetProperty("appVersion").GetString());
    }

    [Fact]
    public void BuildSnapshotMessage_ReflectsMutatedSettings()
    {
        _settings.AutoArrange = true;
        _settings.WindowSizePreset = WindowSizePreset.Large;

        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.True(data.GetProperty("autoArrange").GetBoolean());
        Assert.Equal("Large", data.GetProperty("preset").GetString());
    }

    [Fact]
    public void BuildExternalChangeMessage_HasTypeExternalChange()
    {
        using var doc = JsonDocument.Parse(_bridge.BuildExternalChangeMessage());
        Assert.Equal("externalChange", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(50, doc.RootElement.GetProperty("data").GetProperty("thresholdPx").GetInt32());
    }

    // ---------- apply: toggle-backed keys ----------

    [Fact]
    public void Apply_AutoArrangeDiffers_CallsToggleOnce_AndPersists()
    {
        var response = _bridge.HandleMessage("""{"type":"apply","data":{"autoArrange":true}}""");

        Assert.Contains("ToggleAutoArrange", _controller.Calls);
        Assert.DoesNotContain("ToggleStartWithWindows", _controller.Calls);
        Assert.True(_settings.AutoArrange);
        Assert.True(_settingsService.Load().AutoArrange);            // persisted to disk
        Assert.Equal(new[] { true }, _controller.AutoArrangeEvents); // tray stays in sync

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("applied", doc.RootElement.GetProperty("type").GetString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("autoArrange").GetBoolean());
    }

    [Fact]
    public void Apply_SameValue_DoesNotCallToggle()
    {
        _settings.AutoArrange = false;

        _bridge.HandleMessage("""{"type":"apply","data":{"autoArrange":false}}""");

        Assert.DoesNotContain("ToggleAutoArrange", _controller.Calls);
    }

    [Fact]
    public void Apply_AllFiveToggles_OnlyWhenDiffering()
    {
        _settings.AnimateWindowTransitions = true;
        _settings.DirectionalFocusEnabled = true;
        _settings.StartWithWindows = false;
        _settings.AllowRepositionMaximizedWindows = false;

        var draft = """
            {"type":"apply","data":{
                "autoArrange":true, "animate":false, "directionalFocus":false,
                "startWithWindows":true, "allowRepositionMaximized":true}}
            """;
        _bridge.HandleMessage(draft);

        Assert.Contains("ToggleAutoArrange", _controller.Calls);
        Assert.Contains("ToggleAnimateWindowTransitions", _controller.Calls);
        Assert.Contains("ToggleDirectionalFocus", _controller.Calls);
        Assert.Contains("ToggleStartWithWindows", _controller.Calls);
        Assert.Contains("ToggleAllowRepositionMaximizedWindows", _controller.Calls);
        Assert.Equal(new[] { true }, _controller.StartWithWindowsEvents);
    }

    // ---------- apply: preset ----------

    [Fact]
    public void Apply_PresetChange_CallsSetWindowSizePreset()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"preset":"Medium"}}""");

        Assert.Contains("SetWindowSizePreset:Medium", _controller.Calls);
        Assert.Equal(WindowSizePreset.Medium, _settings.WindowSizePreset);
    }

    [Fact]
    public void Apply_SamePreset_DoesNotRearrange()
    {
        // Plain concatenation: a raw interpolated string ($$") would collide with the
        // literal "}}" that closes the JSON object.
        var json = "{\"type\":\"apply\",\"data\":{\"preset\":\"" + _settings.WindowSizePreset + "\"}}";
        _bridge.HandleMessage(json);

        Assert.DoesNotContain(_controller.Calls, c => c.StartsWith("SetWindowSizePreset"));
    }

    [Fact]
    public void Apply_UnknownPreset_Ignored()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"preset":"Huge"}}""");

        Assert.DoesNotContain(_controller.Calls, c => c.StartsWith("SetWindowSizePreset"));
        Assert.Equal(WindowSizePreset.Small, _settings.WindowSizePreset);
    }

    // ---------- apply: plain fields ----------

    [Fact]
    public void Apply_PlainFields_MutatesSavesOnceAndRaisesOnSettingsChanged()
    {
        var response = _bridge.HandleMessage(
            """{"type":"apply","data":{"enabled":false,"arrangeOnStartup":true,"showTrayNotifications":true,"thresholdPx":80}}""");

        Assert.False(_settings.Enabled);
        Assert.True(_settings.ArrangeOnStartup);
        Assert.True(_settings.ShowTrayNotifications);
        Assert.Equal(80, _settings.DirectionalFocusThresholdPx);
        Assert.Equal(1, _controller.OnSettingsChangedCount);

        var reloaded = _settingsService.Load();                      // exactly one Save
        Assert.False(reloaded.Enabled);
        Assert.True(reloaded.ArrangeOnStartup);
        Assert.Equal(80, reloaded.DirectionalFocusThresholdPx);

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Apply_NonPositiveThreshold_Ignored()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"thresholdPx":0}}""");

        Assert.Equal(50, _settings.DirectionalFocusThresholdPx);
        Assert.Equal(0, _controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_MixedDraft_TogglesPlusPlainFields_AppliesBoth()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"autoArrange":true,"thresholdPx":60}}""");

        Assert.True(_settings.AutoArrange);
        Assert.Equal(60, _settings.DirectionalFocusThresholdPx);
        Assert.Equal(1, _controller.OnSettingsChangedCount);   // plain-field Save happens once
    }

    // ---------- error handling + auxiliary messages ----------

    [Fact]
    public void HandleMessage_MalformedJson_ReturnsErrorAndDoesNotThrow()
    {
        var response = _bridge.HandleMessage("{not json");

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("error", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("malformed-json", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void HandleMessage_MissingType_ReturnsError()
    {
        var response = _bridge.HandleMessage("""{"data":{}}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("missing-type", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void HandleMessage_UnknownType_ReturnsErrorAndIgnores()
    {
        var response = _bridge.HandleMessage("""{"type":"selfDestruct"}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("unknown-type:selfDestruct", doc.RootElement.GetProperty("code").GetString());
        Assert.Empty(_controller.Calls);
    }

    [Fact]
    public void HandleMessage_ApplyWithoutData_ReturnsError()
    {
        var response = _bridge.HandleMessage("""{"type":"apply"}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("apply-without-data", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void HandleMessage_RequestSnapshot_ReturnsSnapshot()
    {
        var response = _bridge.HandleMessage("""{"type":"requestSnapshot"}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("settings", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void HandleMessage_OpenLogFile_CallsController()
    {
        Assert.Null(_bridge.HandleMessage("""{"type":"openLogFile"}"""));
        Assert.Contains("OpenLogFile", _controller.Calls);
    }

    [Fact]
    public void HandleMessage_OpenLogsFolder_CallsController()
    {
        Assert.Null(_bridge.HandleMessage("""{"type":"openLogsFolder"}"""));
        Assert.Contains("OpenLogsFolder", _controller.Calls);
    }

    [Fact]
    public void HandleMessage_OpenSettingsFolder_InvokesInjectedAction()
    {
        int called = 0;
        var bridge = new SettingsBridge(_controller, _settingsService, openSettingsFolder: () => called++);

        Assert.Null(bridge.HandleMessage("""{"type":"openSettingsFolder"}"""));
        Assert.Equal(1, called);
    }

    [Fact]
    public void HandleMessage_DirtyChanged_RaisesDirtyChangedEvent()
    {
        var events = new List<bool>();
        _bridge.DirtyChanged += events.Add;

        _bridge.HandleMessage("""{"type":"dirtyChanged","value":true}""");
        _bridge.HandleMessage("""{"type":"dirtyChanged","value":false}""");

        Assert.Equal(new[] { true, false }, events);
    }

    /// <summary>
    /// Mirrors PeekDowsAppContext semantics: mutate the shared instance, persist via
    /// SettingsService, raise the Changed event. Records call names + counts.
    /// </summary>
    public sealed class FakeBridgeController : IPeekDowsController
    {
        private readonly SettingsService _settingsService;

        public FakeBridgeController(AppSettings settings, SettingsService settingsService)
        {
            CurrentSettings = settings;
            _settingsService = settingsService;
        }

        public List<string> Calls { get; } = new();
        public int OnSettingsChangedCount { get; private set; }
        public List<bool> AutoArrangeEvents { get; } = new();
        public List<bool> StartWithWindowsEvents { get; } = new();

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

        public void ArrangeNow() { Calls.Add(nameof(ArrangeNow)); }
        public void TogglePause() { Calls.Add(nameof(TogglePause)); }
        public void PauseFor(TimeSpan duration) { Calls.Add(nameof(PauseFor)); }
        public void PauseUntilResumed() { Calls.Add(nameof(PauseUntilResumed)); }
        public void Resume() { Calls.Add(nameof(Resume)); }
        public void OpenSettings() { Calls.Add(nameof(OpenSettings)); }
        public void OpenLogFile() { Calls.Add(nameof(OpenLogFile)); }
        public void OpenLogsFolder() { Calls.Add(nameof(OpenLogsFolder)); }
        public void Exit() { Calls.Add(nameof(Exit)); }
        public void OnSettingsChanged() { OnSettingsChangedCount++; }

        public void ToggleAutoArrange()
        {
            Calls.Add(nameof(ToggleAutoArrange));
            CurrentSettings.AutoArrange = !CurrentSettings.AutoArrange;
            _settingsService.Save(CurrentSettings);
            AutoArrangeChanged?.Invoke(CurrentSettings.AutoArrange);
            AutoArrangeEvents.Add(CurrentSettings.AutoArrange);
        }

        public void ToggleStartWithWindows()
        {
            Calls.Add(nameof(ToggleStartWithWindows));
            CurrentSettings.StartWithWindows = !CurrentSettings.StartWithWindows;
            _settingsService.Save(CurrentSettings);
            StartWithWindowsChanged?.Invoke(CurrentSettings.StartWithWindows);
            StartWithWindowsEvents.Add(CurrentSettings.StartWithWindows);
        }

        public void ToggleDirectionalFocus()
        {
            Calls.Add(nameof(ToggleDirectionalFocus));
            CurrentSettings.DirectionalFocusEnabled = !CurrentSettings.DirectionalFocusEnabled;
            _settingsService.Save(CurrentSettings);
            DirectionalFocusChanged?.Invoke(CurrentSettings.DirectionalFocusEnabled);
        }

        public void ToggleAnimateWindowTransitions()
        {
            Calls.Add(nameof(ToggleAnimateWindowTransitions));
            CurrentSettings.AnimateWindowTransitions = !CurrentSettings.AnimateWindowTransitions;
            _settingsService.Save(CurrentSettings);
            AnimateWindowTransitionsChanged?.Invoke(CurrentSettings.AnimateWindowTransitions);
        }

        public void ToggleAllowRepositionMaximizedWindows()
        {
            Calls.Add(nameof(ToggleAllowRepositionMaximizedWindows));
            CurrentSettings.AllowRepositionMaximizedWindows = !CurrentSettings.AllowRepositionMaximizedWindows;
            _settingsService.Save(CurrentSettings);
            AllowRepositionMaximizedWindowsChanged?.Invoke(CurrentSettings.AllowRepositionMaximizedWindows);
        }

        public void SetWindowSizePreset(WindowSizePreset preset)
        {
            Calls.Add($"{nameof(SetWindowSizePreset)}:{preset}");
            CurrentSettings.WindowSizePreset = preset;
            _settingsService.Save(CurrentSettings);
            WindowSizePresetChanged?.Invoke(preset);
        }
    }
}
