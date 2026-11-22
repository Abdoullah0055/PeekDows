using System;
using System.Collections.Generic;
using PeekDows.App.AutoArrange;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class AutoArrangeServiceTests : IDisposable
{
    private readonly FakeController _controller;
    private readonly WindowClassifier _classifier;
    private AutoArrangeService? _service;

    public AutoArrangeServiceTests()
    {
        _controller = new FakeController();
        _classifier = new WindowClassifier(new AppSettings());
    }

    private static WindowDiscoveryService CreateDiscoveryService(params IReadOnlyList<RawWindowInfo>[] snapshots)
    {
        int callIndex = 0;
        IReadOnlyList<RawWindowInfo> Source()
        {
            var result = callIndex < snapshots.Length ? snapshots[callIndex] : [];
            callIndex++;
            return result;
        }
        return new WindowDiscoveryService(Source);
    }

    private static RawWindowInfo MakeRaw(
        IntPtr hwnd,
        string title = "Test Window",
        string className = "Chrome_WidgetWin_1",
        string processName = "chrome.exe")
    {
        return new RawWindowInfo
        {
            Hwnd = hwnd,
            Title = title,
            ClassName = className,
            ProcessId = 1234,
            ProcessName = processName,
            CurrentRect = new Rect(0, 0, 800, 600),
            IsVisible = true,
            IsMinimized = false,
            IsMaximized = false,
            IsForeground = false,
            IsCloaked = false
        };
    }

    private AutoArrangeService CreateService(WindowDiscoveryService discoveryService, Func<bool>? isPreviewOpen = null)
    {
        return new AutoArrangeService(discoveryService, _classifier, _controller, new FileLogger(), isPreviewOpen);
    }

    [Fact]
    public void OpenPreviewPausesDiscoveryAndTheFirstTickAfterCloseSchedulesOnce()
    {
        IReadOnlyList<RawWindowInfo> windows = [MakeRaw((IntPtr)1)];
        int reads = 0;
        var discovery = new WindowDiscoveryService(() => { reads++; return windows; });
        bool previewOpen = false;
        _service = CreateService(discovery, () => previewOpen);
        _controller.CurrentSettingsValue.AutoArrange = true;
        _service.Start(_controller.CurrentSettings);

        previewOpen = true;
        windows = [.. windows, MakeRaw((IntPtr)2)];
        _service.Tick();
        _service.TickPendingDelay();
        Assert.Equal(1, reads);
        Assert.Equal(0, _controller.ArrangeNowCallCount);

        previewOpen = false;
        _service.Tick();
        Assert.Equal(2, reads);
        Assert.Equal(0, _controller.ArrangeNowCallCount);
        _service.TickPendingDelay();
        _service.TickPendingDelay();
        Assert.Equal(1, _controller.ArrangeNowCallCount);
    }

    [Theory]
    [InlineData("active", 1)]
    [InlineData("paused", 0)]
    [InlineData("stopped", 0)]
    public void PendingDelayWaitsForPreviewUnlessAutoArrangeWasPausedOrStopped(string state, int expectedArranges)
    {
        IReadOnlyList<RawWindowInfo> windows = [MakeRaw((IntPtr)1)];
        var discovery = new WindowDiscoveryService(() => windows);
        bool previewOpen = false;
        _service = CreateService(discovery, () => previewOpen);
        _controller.CurrentSettingsValue.AutoArrange = true;
        _service.Start(_controller.CurrentSettings);
        windows = [.. windows, MakeRaw((IntPtr)2)];
        _service.Tick();

        previewOpen = true;
        _service.TickPendingDelay();
        _service.TickPendingDelay();
        Assert.Equal(0, _controller.ArrangeNowCallCount);

        if (state == "paused")
        {
            _controller.SetState(RuntimeState.Paused);
            _controller.SetState(RuntimeState.Running);
        }
        else if (state == "stopped")
        {
            _service.Stop();
        }

        previewOpen = false;
        _service.TickPendingDelay();
        _service.TickPendingDelay();
        Assert.Equal(expectedArranges, _controller.ArrangeNowCallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemovalAfterCancelledPreview_JoinsPendingStabilizationInEitherTimerOrder(bool discoveryTicksFirst)
    {
        var a = MakeRaw((IntPtr)1);
        var b = MakeRaw((IntPtr)2);
        IReadOnlyList<RawWindowInfo> windows = [a];
        var discovery = new WindowDiscoveryService(() => windows);
        bool previewOpen = false;
        _service = CreateService(discovery, () => previewOpen);
        _controller.CurrentSettingsValue.AutoArrange = true;
        _controller.CurrentSettingsValue.ArrangeAfterWindowCloses = true;
        _service.Start(_controller.CurrentSettings);
        windows = [a, b];
        _service.Tick();

        previewOpen = true;
        windows = [b];
        _service.Tick();
        Assert.Equal(0, _controller.ArrangeNowCallCount);

        previewOpen = false;
        if (discoveryTicksFirst)
        {
            _service.Tick();
            Assert.Equal(0, _controller.ArrangeNowCallCount);
        }

        _service.TickPendingDelay();
        Assert.Equal(1, _controller.ArrangeNowCallCount);
        if (!discoveryTicksFirst)
            _service.Tick();
        _service.TickPendingDelay();
        Assert.Equal(1, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void AppliedPreviewClearsPendingDelayButStillDetectsWindowsOpenedAfterValidation()
    {
        IReadOnlyList<RawWindowInfo> windows = [MakeRaw((IntPtr)1)];
        var discovery = new WindowDiscoveryService(() => windows);
        bool previewOpen = false;
        _service = CreateService(discovery, () => previewOpen);
        _controller.CurrentSettingsValue.AutoArrange = true;
        _service.Start(_controller.CurrentSettings);
        windows = [.. windows, MakeRaw((IntPtr)2)];
        _service.Tick();

        previewOpen = true;
        var validated = discovery.Fork();
        windows = [.. windows, MakeRaw((IntPtr)3)];
        var validatedWindows = validated.Refresh(_classifier).Current;
        windows = [.. windows, MakeRaw((IntPtr)4)];
        Assert.True(_service.AcknowledgePreviewPlacement(validated,
            new PlacementResult { SucceededHwnds = [(IntPtr)2] }));

        Assert.Equal(validatedWindows.Select(window => window.Hwnd), discovery.GetKnownWindows().Select(window => window.Hwnd));
        Assert.NotSame(validatedWindows[0], discovery.GetKnownWindows()[0]);
        previewOpen = false;
        _service.TickPendingDelay();
        Assert.Equal(0, _controller.ArrangeNowCallCount);
        _service.Tick();
        _service.TickPendingDelay();
        _service.TickPendingDelay();
        Assert.Equal(1, _controller.ArrangeNowCallCount);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("stale")]
    [InlineData("no accepted placements")]
    public void UnappliedPreviewKeepsBothTheBaselineAndPendingDelay(string outcome)
    {
        IReadOnlyList<RawWindowInfo> windows = [MakeRaw((IntPtr)1)];
        var discovery = new WindowDiscoveryService(() => windows);
        bool previewOpen = false;
        _service = CreateService(discovery, () => previewOpen);
        _controller.CurrentSettingsValue.AutoArrange = true;
        _service.Start(_controller.CurrentSettings);
        windows = [.. windows, MakeRaw((IntPtr)2)];
        _service.Tick();

        previewOpen = true;
        var preview = discovery.Fork();
        windows = [.. windows, MakeRaw((IntPtr)3)];
        preview.Refresh(_classifier);
        if (outcome == "stale")
        {
            windows = [windows[0], windows[2]];
            preview.Refresh(_classifier);
        }
        _service.TickPendingDelay();
        var validated = outcome == "no accepted placements" ? preview : null;
        Assert.False(_service.AcknowledgePreviewPlacement(validated,
            new PlacementResult { ChangedHwnds = [(IntPtr)2] }));

        Assert.Equal(new IntPtr[] { (IntPtr)1, (IntPtr)2 }, discovery.GetKnownWindows().Select(window => window.Hwnd));
        Assert.Equal(0, _controller.ArrangeNowCallCount);
        previewOpen = false;
        _service.TickPendingDelay();
        _service.TickPendingDelay();
        Assert.Equal(1, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void Baseline_ExistingWindowsDoNotTriggerArrangeAfterStart()
    {
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        var discovery = CreateDiscoveryService(snapshot, snapshot);
        _service = CreateService(discovery);

        var settings = new AppSettings
        {
            Enabled = true,
            AutoArrange = true,
            ArrangeOnStartup = false
        };

        _service.Start(settings);
        _controller.CurrentSettingsValue = settings;
        _service.Tick();

        Assert.Equal(0, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void Baseline_IsRecreatedAfterStopRestart()
    {
        int callIndex = 0;
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        IReadOnlyList<RawWindowInfo> Source()
        {
            callIndex++;
            return snapshot;
        }

        var discovery = new WindowDiscoveryService(Source);
        _service = CreateService(discovery);

        var settings = new AppSettings { Enabled = true, AutoArrange = true };

        _service.Start(settings);
        _service.Stop();
        _service.Start(settings);

        Assert.Equal(2, callIndex);
        Assert.Equal(0, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void NewWindowAfterBaseline_DoesNotTriggerImmediateArrange()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome"),
            MakeRaw((IntPtr)200, title: "VS Code")
        };

        var discovery = CreateDiscoveryService(snapshot1, snapshot2);
        _service = CreateService(discovery);

        var settings = new AppSettings
        {
            Enabled = true,
            AutoArrange = true,
            ArrangeOnStartup = false
        };

        _service.Start(settings);
        _controller.CurrentSettingsValue = settings;
        _service.Tick();

        Assert.Equal(0, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void RemovedWindowAfterBaseline_TriggersImmediateArrange()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome"),
            MakeRaw((IntPtr)200, title: "VS Code")
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        var discovery = CreateDiscoveryService(snapshot1, snapshot2);
        _service = CreateService(discovery);

        var settings = new AppSettings
        {
            Enabled = true,
            AutoArrange = true,
            ArrangeAfterWindowCloses = true
        };

        _service.Start(settings);
        _controller.CurrentSettingsValue = settings;
        _service.Tick();

        Assert.Equal(1, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void RemovedWindow_ArrangeAfterCloseFalse_DoesNotArrange()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome"),
            MakeRaw((IntPtr)200, title: "VS Code")
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        var discovery = CreateDiscoveryService(snapshot1, snapshot2);
        _service = CreateService(discovery);

        var settings = new AppSettings
        {
            Enabled = true,
            AutoArrange = true,
            ArrangeAfterWindowCloses = false
        };

        _service.Start(settings);
        _controller.CurrentSettingsValue = settings;
        _service.Tick();

        Assert.Equal(0, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void Start_Idempotent_DoesNotCallRefreshTwice()
    {
        int callIndex = 0;
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        IReadOnlyList<RawWindowInfo> Source()
        {
            callIndex++;
            return snapshot;
        }

        var discovery = new WindowDiscoveryService(Source);
        _service = CreateService(discovery);

        var settings = new AppSettings { Enabled = true, AutoArrange = true };

        _service.Start(settings);
        _service.Start(settings);

        Assert.Equal(1, callIndex);
    }

    [Fact]
    public void WindowsOpenedWhileStopped_AreBaselinedOnRestart()
    {
        var snapshotA = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        var snapshotB = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome"),
            MakeRaw((IntPtr)200, title: "VS Code")
        };

        int callIndex = 0;
        IReadOnlyList<RawWindowInfo> Source()
        {
            callIndex++;
            return callIndex <= 1 ? snapshotA : snapshotB;
        }

        var discovery = new WindowDiscoveryService(Source);
        _service = CreateService(discovery);

        var settings = new AppSettings
        {
            Enabled = true,
            AutoArrange = true,
            ArrangeOnStartup = false
        };

        _service.Start(settings);
        _service.Stop();

        _service.Start(settings);
        _controller.CurrentSettingsValue = settings;
        _service.Tick();

        Assert.Equal(0, _controller.ArrangeNowCallCount);
    }

    [Fact]
    public void AutoArrange_WhenControllerPaused_DoesNotArrange()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome")
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome"),
            MakeRaw((IntPtr)200, title: "VS Code")
        };

        var discovery = CreateDiscoveryService(snapshot1, snapshot2);
        _service = CreateService(discovery);

        var settings = new AppSettings
        {
            Enabled = true,
            AutoArrange = true,
            ArrangeOnStartup = false
        };

        _service.Start(settings);
        _controller.State = RuntimeState.Paused;
        _controller.CurrentSettingsValue = settings;
        _service.Tick();

        Assert.Equal(0, _controller.ArrangeNowCallCount);
    }

    public void Dispose()
    {
        _service?.Dispose();
    }

    private class FakeController : IPeekDowsController
    {
        public RuntimeState State { get; set; } = RuntimeState.Running;
        public string LogFilePath => "";
        public AppSettings CurrentSettingsValue { get; set; } = new();
        public AppSettings CurrentSettings => CurrentSettingsValue;
        public bool IsAutoArrangeRunning => false;
        public bool IsStartWithWindowsEnabled => false;
        public bool IsDirectionalFocusEnabled => false;
        public bool IsAnimateWindowTransitionsEnabled => true;
        public bool AllowRepositionMaximizedWindows => CurrentSettingsValue.AllowRepositionMaximizedWindows;
        public WindowSizePreset CurrentWindowSizePreset => WindowSizePreset.Small;
        public bool IsPaused => State == RuntimeState.Paused;
        public DateTimeOffset? PauseUntil => null;
        public string? PauseDescription => null;
        public int ArrangeNowCallCount { get; private set; }

        public event Action<RuntimeState>? StateChanged;
        public event Action<bool>? AutoArrangeChanged;
        public event Action<bool>? StartWithWindowsChanged;
        public event Action<bool>? DirectionalFocusChanged;
        public event Action<bool>? AnimateWindowTransitionsChanged;
        public event Action<bool>? AllowRepositionMaximizedWindowsChanged;
        public event Action<WindowSizePreset>? WindowSizePresetChanged;
        public event Action<string>? FocusHintModeChanged;
        public event Action<bool>? SoundChanged;

        public void ArrangeNow() => ArrangeNowCallCount++;

        public void SetState(RuntimeState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
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
        public void ToggleArrangeSound() { CurrentSettingsValue.ArrangeSoundEnabled = !CurrentSettingsValue.ArrangeSoundEnabled; }
        public void RefreshArrangeSoundAutoOff() { }
        public void DisableArrangeSound(string reason) { CurrentSettingsValue.ArrangeSoundEnabled = false; }
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

        public void OnSettingsChanged() { }
        public void Exit() { }
    }
}
