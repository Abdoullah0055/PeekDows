using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Forms;
using PeekDows.App;
using PeekDows.App.Hotkeys;
using PeekDows.App.Startup;
using PeekDows.App.Settings;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Covers the pure JS⇄C# settings bridge. The real PeekDowsAppContext is wired to live
/// Win32 services, so the controller double calls the production settings transaction
/// with temporary files and simulated Windows registrations.
/// </summary>
public class SettingsBridgeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settingsService;
    private AppSettings Settings => _controller.CurrentSettings;
    private readonly FakeBridgeController _controller;
    private readonly SettingsBridge _bridge;

    public SettingsBridgeTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(System.IO.Path.Combine(_tempDir, "settings.json"));
        _controller = new FakeBridgeController(_settingsService.Load(), _settingsService);
        _bridge = new SettingsBridge(_controller, appVersionProvider: () => "9.9.9");
    }

    public void Dispose()
    {
        _controller.Dispose();
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
        Assert.Equal(5, data.GetProperty("version").GetInt32());
        Assert.Equal("9.9.9", data.GetProperty("appVersion").GetString());
        Assert.Equal("Overlay", data.GetProperty("focusHintMode").GetString());
        Assert.True(data.GetProperty("overlayShowIcons").GetBoolean());
        Assert.False(data.TryGetProperty("spotlightOpacity", out _));
    }

    [Fact]
    public void BuildSnapshotMessage_ReflectsMutatedSettings()
    {
        Settings.AutoArrange = true;
        Settings.WindowSizePreset = WindowSizePreset.Large;

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
    public void Apply_AutoArrangeDiffers_CommitsOnceAndPersists()
    {
        var response = _bridge.HandleMessage("""{"type":"apply","data":{"autoArrange":true}}""");

        Assert.Equal(1, _controller.CommitCount);
        Assert.Equal(1, _controller.SaveCount);
        Assert.False(Settings.StartWithWindows);
        Assert.True(Settings.AutoArrange);
        Assert.True(_settingsService.Load().AutoArrange);            // persisted to disk

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("applied", doc.RootElement.GetProperty("type").GetString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("autoArrange").GetBoolean());
    }

    [Fact]
    public void Apply_SameValue_DoesNotCommit()
    {
        Settings.AutoArrange = false;

        _bridge.HandleMessage("""{"type":"apply","data":{"autoArrange":false}}""");

        Assert.Equal(0, _controller.CommitCount);
        Assert.Equal(0, _controller.SaveCount);
    }

    [Fact]
    public void Apply_AllFiveToggles_CommitsTogether()
    {
        Settings.AnimateWindowTransitions = true;
        Settings.DirectionalFocusEnabled = true;
        Settings.StartWithWindows = false;
        Settings.AllowRepositionMaximizedWindows = false;

        var draft = """
            {"type":"apply","data":{
                "autoArrange":true, "animate":false, "directionalFocus":false,
                "startWithWindows":true, "allowRepositionMaximized":true}}
            """;
        _bridge.HandleMessage(draft);

        var saved = _settingsService.Load();
        Assert.True(saved.AutoArrange);
        Assert.False(saved.AnimateWindowTransitions);
        Assert.False(saved.DirectionalFocusEnabled);
        Assert.True(saved.StartWithWindows);
        Assert.True(saved.AllowRepositionMaximizedWindows);
        Assert.Equal(1, _controller.CommitCount);
        Assert.Equal(1, _controller.SaveCount);
    }

    // ---------- apply: preset ----------

    [Fact]
    public void Apply_PresetChange_CommitsAndArrangesOnce()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"preset":"Medium"}}""");

        Assert.Equal(1, _controller.CommitCount);
        Assert.Single(_controller.Arrangements);
        Assert.Equal(WindowSizePreset.Medium, Settings.WindowSizePreset);
    }

    [Fact]
    public void Apply_SamePreset_DoesNotRearrange()
    {
        // Plain concatenation: a raw interpolated string ($$") would collide with the
        // literal "}}" that closes the JSON object.
        var json = "{\"type\":\"apply\",\"data\":{\"preset\":\"" + Settings.WindowSizePreset + "\"}}";
        _bridge.HandleMessage(json);

        Assert.Equal(0, _controller.CommitCount);
        Assert.Empty(_controller.Arrangements);
    }

    [Fact]
    public void Apply_UnknownPreset_Ignored()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"preset":"Huge"}}""");

        Assert.Equal(0, _controller.CommitCount);
        Assert.Empty(_controller.Arrangements);
        Assert.Equal(WindowSizePreset.Small, Settings.WindowSizePreset);
    }

    [Fact]
    public void Apply_MixedLayoutSettings_ArrangesOnceWithCommittedRulesAndPreset()
    {
        var response = _bridge.HandleMessage(
            """{"type":"apply","data":{"preset":"Medium","layoutMode":"Halves","appRules":[{"process":"notes.exe","action":"ignore"}],"hotkeys":{"arrangeNow":"Ctrl+Alt+F8"}}}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var arranged = Assert.Single(_controller.Arrangements);
        Assert.Equal(("Halves", WindowSizePreset.Medium, "notes.exe"), arranged);
        var saved = _settingsService.Load();
        Assert.Equal("Halves", saved.LayoutMode);
        Assert.Equal(WindowSizePreset.Medium, saved.WindowSizePreset);
        Assert.Equal("notes.exe", Assert.Single(saved.AppRules).Process);
        Assert.Equal("Ctrl+Alt+F8", saved.Hotkeys["arrangeNow"]);
        Assert.Equal("Ctrl+Alt+1", saved.Hotkeys["assignSlotA"]);
        Assert.Equal("Ctrl+Alt+F8", _controller.HotkeyApi.RegisteredGesture(1));
        Assert.Equal(1, _controller.CommitCount);
        Assert.Equal(1, _controller.SaveCount);
    }

    [Fact]
    public void Apply_DisabledWithNewPreset_SavesWithoutArranging()
    {
        var response = _bridge.HandleMessage("""{"type":"apply","data":{"enabled":false,"preset":"Large"}}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.False(_controller.CurrentSettings.Enabled);
        Assert.False(_settingsService.Load().Enabled);
        Assert.Equal(1, _controller.CommitCount);
        Assert.Equal(1, _controller.SaveCount);
        Assert.Empty(_controller.Arrangements);
    }

    [Fact]
    public void Apply_WriteFailure_LeavesLiveSettingsUntouched()
    {
        var invalidDestination = new SettingsService(System.IO.Directory.CreateDirectory(
            System.IO.Path.Combine(_tempDir, "invalid-destination")).FullName);
        using var controller = new FakeBridgeController(Settings, invalidDestination);
        var bridge = new SettingsBridge(controller);

        var response = bridge.HandleMessage("""{"type":"apply","data":{"enabled":false}}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("applied", doc.RootElement.GetProperty("type").GetString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("settings-write-failed", doc.RootElement.GetProperty("error").GetString());
        Assert.True(controller.CurrentSettings.Enabled);
        Assert.Equal(1, controller.CommitCount);
        Assert.Equal(1, controller.SaveCount);
        Assert.Equal(0, controller.PublishCount);
        Assert.Empty(controller.Arrangements);
    }

    [Fact]
    public void Apply_HotkeyConflict_LeavesAnimationAndOldHotkeyUntouched()
    {
        _controller.HotkeyApi.TakeGesture("Ctrl+Alt+F9");
        var before = Settings.Hotkeys["arrangeNow"];

        var response = _bridge.HandleMessage(
            """{"type":"apply","data":{"animate":false,"hotkeys":{"arrangeNow":"Ctrl+Alt+F9"}}}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("applied", doc.RootElement.GetProperty("type").GetString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("hotkey-conflict", doc.RootElement.GetProperty("error").GetString());
        Assert.True(_controller.CurrentSettings.AnimateWindowTransitions);
        Assert.Equal(before, _controller.CurrentSettings.Hotkeys["arrangeNow"]);
        Assert.True(_settingsService.Load().AnimateWindowTransitions);
        Assert.Equal(before, _controller.HotkeyApi.RegisteredGesture(1));
        Assert.Equal(1, _controller.CommitCount);
        Assert.Equal(0, _controller.SaveCount);
        Assert.Equal(0, _controller.PublishCount);
    }

    [Fact]
    public async Task LegacySave_AppliesOnlyIgnoredListsToLatestSettings()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsLegacyWindow(_controller);
                Assert.True(_controller.TryCommitSettings(new AppSettings { LayoutMode = "Halves" }, out _));
                var ignoredPage = form.Controls.OfType<TabControl>().Single().TabPages
                    .Cast<TabPage>().Single(page => page.Text == "Ignored apps");
                ignoredPage.Controls.OfType<TextBox>().First().Text = "notes.exe";

                Assert.True(form.TrySaveIgnoredLists(out var error), error);
                var saved = _settingsService.Load();
                Assert.Equal("Halves", saved.LayoutMode);
                Assert.Equal(["notes.exe"], saved.IgnoredProcesses);
                Assert.Equal(2, _controller.CommitCount);
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    // ---------- apply: plain fields ----------

    [Fact]
    public void Apply_PlainFields_MutatesSavesOnceAndRaisesOnSettingsChanged()
    {
        var response = _bridge.HandleMessage(
            """{"type":"apply","data":{"enabled":false,"arrangeOnStartup":true,"showTrayNotifications":true,"thresholdPx":80}}""");

        Assert.False(Settings.Enabled);
        Assert.True(Settings.ArrangeOnStartup);
        Assert.True(Settings.ShowTrayNotifications);
        Assert.Equal(80, Settings.DirectionalFocusThresholdPx);
        Assert.Equal(1, _controller.PublishCount);

        Assert.Equal(1, _controller.SaveCount);
        var reloaded = _settingsService.Load();
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

        Assert.Equal(50, Settings.DirectionalFocusThresholdPx);
        Assert.Equal(0, _controller.PublishCount);
    }

    [Fact]
    public void Apply_MixedDraft_TogglesPlusPlainFields_AppliesBoth()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"autoArrange":true,"thresholdPx":60}}""");

        Assert.True(Settings.AutoArrange);
        Assert.Equal(60, Settings.DirectionalFocusThresholdPx);
        Assert.Equal(1, _controller.CommitCount);
        Assert.Equal(1, _controller.SaveCount);
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
        Assert.Equal("apply-without-data", doc.RootElement.GetProperty("error").GetString());
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
        var bridge = new SettingsBridge(_controller, openSettingsFolder: () => called++);

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

    internal sealed class FakeBridgeController : IPeekDowsController, IDisposable
    {
        private readonly SettingsService _settingsService;
        private readonly HotkeyService _hotkeys;
        private readonly StartupService _startup;

        public FakeBridgeController(AppSettings settings, SettingsService settingsService)
        {
            CurrentSettings = settings;
            _settingsService = settingsService;
            var folder = System.IO.Path.GetDirectoryName(settingsService.SettingsFilePath)!;
            _startup = SettingsCommitterTests.CreateStartupService(folder);
            _hotkeys = new HotkeyService(HotkeyApi.Register, HotkeyApi.Unregister);
            var initial = PeekDowsAppContext.DefaultHotkeys.ToDictionary(
                entry => entry.Key, entry => settings.Hotkeys.GetValueOrDefault(entry.Key, entry.Value));
            Assert.True(_hotkeys.TryUpdateGestures(initial, out var error), error);
        }

        public List<string> Calls { get; } = new();
        public int CommitCount { get; private set; }
        public int SaveCount { get; private set; }
        public int PublishCount { get; private set; }
        public List<(string Layout, WindowSizePreset Preset, string? IgnoredProcess)> Arrangements { get; } = new();
        public SettingsCommitterTests.SimulatedHotkeyApi HotkeyApi { get; } = new();

        public RuntimeState State => IsPaused ? RuntimeState.Paused : RuntimeState.Running;
        public string LogFilePath => "";
        public AppSettings CurrentSettings { get; private set; }
        public bool IsAutoArrangeRunning => false;
        public bool IsStartWithWindowsEnabled => CurrentSettings.StartWithWindows;
        public bool IsDirectionalFocusEnabled => CurrentSettings.DirectionalFocusEnabled;
        public bool IsAnimateWindowTransitionsEnabled => CurrentSettings.AnimateWindowTransitions;
        public bool AllowRepositionMaximizedWindows => CurrentSettings.AllowRepositionMaximizedWindows;
        public WindowSizePreset CurrentWindowSizePreset => CurrentSettings.WindowSizePreset;
        public bool IsPaused { get; set; }
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
        public event Action<string>? LayoutModeChanged;
        public event Action<bool>? SoundChanged;

        public bool TryCommitSettings(AppSettings next, out string error)
        {
            CommitCount++;
            return SettingsCommitter.TryCommit(CurrentSettings, next, _startup,
                _hotkeys.TryUpdateGestures, settings =>
                {
                    SaveCount++;
                    _settingsService.Save(settings);
                }, settings =>
                {
                    CurrentSettings = settings;
                    PublishCount++;
                }, () => IsPaused, ArrangeNow, (_, _) => { }, out error);
        }

        public void ArrangeNow() => Arrangements.Add((CurrentSettings.LayoutMode, CurrentSettings.WindowSizePreset,
            CurrentSettings.AppRules.Find(rule => rule.Action == "ignore")?.Process));
        public void TogglePause() { Calls.Add(nameof(TogglePause)); }
        public void PauseFor(TimeSpan duration) { Calls.Add(nameof(PauseFor)); }
        public void PauseUntilResumed() { Calls.Add(nameof(PauseUntilResumed)); }
        public void Resume() { Calls.Add(nameof(Resume)); }
        public void OpenSettings() { Calls.Add(nameof(OpenSettings)); }
        public void OpenLogFile() { Calls.Add(nameof(OpenLogFile)); }
        public void OpenLogsFolder() { Calls.Add(nameof(OpenLogsFolder)); }
        public void Exit() { Calls.Add(nameof(Exit)); }
        public void OnSettingsChanged() => throw new NotSupportedException();
        public void ToggleAutoArrange() => throw new NotSupportedException();
        public void ToggleStartWithWindows() => throw new NotSupportedException();
        public void ToggleDirectionalFocus() => throw new NotSupportedException();
        public void ToggleAnimateWindowTransitions() => throw new NotSupportedException();
        public void ToggleAllowRepositionMaximizedWindows() => throw new NotSupportedException();
        public void SetFocusHintMode(string mode) => throw new NotSupportedException();
        public void UndoArrange() { }
        public bool CanUndoArrange => false;
        public void SetLayoutMode(string mode) => throw new NotSupportedException();
        public void ToggleArrangeSound() => throw new NotSupportedException();
        public void RefreshArrangeSoundAutoOff() => throw new NotSupportedException();
        public void DisableArrangeSound(string reason) => throw new NotSupportedException();
        public string[] UserSounds { get; set; } = [];
        public string PickedSound { get; set; } = "";
        public Exception? SoundPickError { get; set; }
        public Exception? SoundDeleteError { get; set; }
        public string[] GetUserSounds() => UserSounds;
        public string PickArrangeSoundFile()
        {
            Calls.Add(nameof(PickArrangeSoundFile));
            if (SoundPickError is { } error) throw error;
            return PickedSound;
        }
        public void DeleteArrangeSoundFile(string name)
        {
            if (SoundDeleteError is { } error) throw error;
        }
        public void PreviewArrangeSound(string name) { }
        public void SetWindowSizePreset(WindowSizePreset preset) => throw new NotSupportedException();
        public void Dispose() => _hotkeys.Dispose();
    }
}
