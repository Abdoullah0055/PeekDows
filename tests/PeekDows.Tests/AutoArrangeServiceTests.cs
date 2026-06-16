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

    private AutoArrangeService CreateService(WindowDiscoveryService discoveryService)
    {
        return new AutoArrangeService(discoveryService, _classifier, _controller, new FileLogger());
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
    public void Baseline_OnlyCalledOnceEvenAfterStopRestart()
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

        Assert.Equal(1, callIndex);
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
        public int ArrangeNowCallCount { get; private set; }

        public event Action<RuntimeState>? StateChanged;
        public event Action<bool>? AutoArrangeChanged;

        public void ArrangeNow() => ArrangeNowCallCount++;
        public void TogglePause() { }
        public void ToggleAutoArrange() { }
        public void OpenSettings() { }
        public void OpenLogFile() { }
        public void OpenLogsFolder() { }
        public void Exit() { }
    }
}
