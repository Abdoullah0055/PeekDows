using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PeekDows.App;
using PeekDows.App.Settings;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public sealed class SettingsBridgeWindowsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsEligibility_UsesEffectiveRulesOnEachDisplay(bool globalIgnore)
    {
        var left = new MonitorInfo { Handle = (IntPtr)1, ProfileId = "left", WorkArea = new(0, 0, 1200, 900) };
        var right = new MonitorInfo { Handle = (IntPtr)2, ProfileId = "right", WorkArea = new(1200, 0, 1200, 900) };
        MonitorInfo ResolveMonitor(IntPtr hwnd) => hwnd == (IntPtr)1 ? left : right;
        var settings = new AppSettings
        {
            AppRules = [new() { Process = "notes.exe", Action = globalIgnore ? "ignore" : "pin", Slot = "A" }],
            MonitorProfiles = new()
            {
                ["left"] = new("Grid", [new() { Process = "notes", Action = "ignore" }])
            }
        };
        if (globalIgnore)
            settings.MonitorProfiles["right"] = new("Grid", [new() { Process = "notes", Action = "pin", Slot = "A" }]);
        var windows = new[] { TestWindow(1), TestWindow(2) };
        var discovery = new WindowDiscoveryService(() => windows);
        var classifier = new WindowClassifier(settings);
        var policy = new LoneWindowMaximizePolicy();

        var rows = WindowsSnapshotProvider.Build(discovery, classifier, 200,
            PeekDowsAppContext.CreateWindowsPlacementCheck(settings, ResolveMonitor, policy));
        var excluded = rows.Single(row => row.Hwnd == "1");
        var allowed = rows.Single(row => row.Hwnd == "2");
        Assert.False(excluded.Eligible);
        Assert.True(excluded.HasProfileRule);
        Assert.Equal("ignored-by-profile", excluded.Reason);
        Assert.True(allowed.Eligible);
        Assert.Equal(globalIgnore, allowed.HasProfileRule);
    }

    [Fact]
    public void WindowsEligibility_UsesPlacementCheckOnlyAfterBaseEligibility()
    {
        var settings = new AppSettings { IgnoredProcesses = ["notes.exe"] };
        var discovery = new WindowDiscoveryService(() => [TestWindow(1), TestWindow(2)]);
        var rows = WindowsSnapshotProvider.Build(discovery, new WindowClassifier(settings), 1,
            placementReason: _ => throw new InvalidOperationException("Base exclusion must take precedence."));

        var row = Assert.Single(rows);
        Assert.Equal("ignored-process", row.Reason);
        Assert.False(row.Eligible);
        Assert.False(row.HasProfileRule);
    }

    [Fact]
    public void WindowsEligibility_KeepsDisplayRuleProvenanceAfterSavingProcessExclusion()
    {
        var left = new MonitorInfo { Handle = (IntPtr)1, ProfileId = "left", WorkArea = new(0, 0, 1200, 900) };
        var right = new MonitorInfo { Handle = (IntPtr)2, ProfileId = "right", WorkArea = new(1200, 0, 1200, 900) };
        var settings = new AppSettings
        {
            AppRules = [new() { Process = "notes.exe", Action = "ignore" }],
            MonitorProfiles = new()
            {
                ["right"] = new("Grid", [new() { Process = "notes", Action = "pin", Slot = "A" }])
            },
            IgnoredProcesses = ["notes.exe"]
        };
        var discovery = new WindowDiscoveryService(() =>
            [TestWindow(1), TestWindow(2, maximized: true), TestWindow(3, className: "Shell_TrayWnd"),
                TestWindow(4, processName: "SearchHost.exe")]);
        var checkedWindows = new List<IntPtr>();
        var rows = WindowsSnapshotProvider.Build(discovery, new WindowClassifier(settings), 200,
            PeekDowsAppContext.CreateWindowsPlacementCheck(settings, hwnd =>
            {
                checkedWindows.Add(hwnd);
                return hwnd == (IntPtr)1 ? left : right;
            }, new LoneWindowMaximizePolicy())).ToDictionary(row => row.Hwnd);

        Assert.All(rows.Values, row => Assert.False(row.Eligible));
        Assert.Equal("ignored-process", rows["1"].Reason);
        Assert.False(rows["1"].HasProfileRule);
        Assert.Equal("ignored-process", rows["2"].Reason);
        Assert.True(rows["2"].HasProfileRule);
        Assert.Equal("system", rows["3"].Reason);
        Assert.Equal("ignored-process", rows["4"].Reason);
        Assert.Equal(new[] { (IntPtr)1, (IntPtr)2 }, checkedWindows);
    }

    [Fact]
    public void WindowsEligibility_AllowsPeekDowsMaximizedWindowsWithRepositionSettingOff()
    {
        var settings = new AppSettings { AllowRepositionMaximizedWindows = false };
        var monitor = new MonitorInfo { Handle = (IntPtr)1, WorkArea = new(0, 0, 1200, 900) };
        var discovery = new WindowDiscoveryService(() => [TestWindow(1, true), TestWindow(2, true)]);
        var policy = new LoneWindowMaximizePolicy();
        policy.NotifyMaximizedByPeekDows((IntPtr)2);

        var rows = WindowsSnapshotProvider.Build(discovery, new WindowClassifier(settings), 200,
            PeekDowsAppContext.CreateWindowsPlacementCheck(settings, _ => monitor, policy));

        Assert.Equal("maximized", rows.Single(row => row.Hwnd == "1").Reason);
        Assert.True(rows.Single(row => row.Hwnd == "2").Eligible);
    }

    private static RawWindowInfo TestWindow(int handle, bool maximized = false,
        string className = "", string processName = "notes.exe") => new()
    {
        Hwnd = (IntPtr)handle,
        Title = $"Notes {handle}",
        ProcessId = Environment.ProcessId + 1,
        ProcessName = processName,
        ClassName = className,
        CurrentRect = new(0, 0, 800, 600),
        IsVisible = true,
        IsMaximized = maximized
    };

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
        var bridge = new SettingsBridge(tmp.Controller, appVersionProvider: () => "9.9.9");
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
        public event Action<bool>? SoundChanged;

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
        public void UndoArrange() { }
        public bool CanUndoArrange => false;
        public void SetLayoutMode(string mode) { CurrentSettings.LayoutMode = mode; }
        public event Action<string>? LayoutModeChanged;
        public void ToggleArrangeSound()
        {
            CurrentSettings.ArrangeSoundEnabled = !CurrentSettings.ArrangeSoundEnabled;
            _settingsService.Save(CurrentSettings);
            SoundChanged?.Invoke(CurrentSettings.ArrangeSoundEnabled);
        }
        public void RefreshArrangeSoundAutoOff() { }
        public void DisableArrangeSound(string reason)
        {
            CurrentSettings.ArrangeSoundEnabled = false;
            _settingsService.Save(CurrentSettings);
            SoundChanged?.Invoke(false);
        }
        public string[] GetUserSounds() => Array.Empty<string>();
        public string PickArrangeSoundFile() => "";
        public void DeleteArrangeSoundFile(string name) { }
        public void PreviewArrangeSound(string name) { }

        public void SetWindowSizePreset(WindowSizePreset preset) { }
        public void OpenSettings() { }
        public void OpenLogFile() { }
        public void OpenLogsFolder() { }
        public bool TryCommitSettings(AppSettings next, out string error)
            => throw new NotSupportedException();

        public void OnSettingsChanged() { OnSettingsChangedCount++; }
        public void Exit() { }
    }
}
