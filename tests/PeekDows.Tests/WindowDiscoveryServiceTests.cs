using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class WindowDiscoveryServiceTests
{
    private readonly AppSettings _settings;
    private readonly WindowClassifier _classifier;

    public WindowDiscoveryServiceTests()
    {
        _settings = new AppSettings();
        _classifier = new WindowClassifier(_settings);
    }

    private static RawWindowInfo MakeRaw(
        IntPtr hwnd,
        string title = "Test Window",
        string className = "Chrome_WidgetWin_1",
        string processName = "chrome.exe",
        bool isVisible = true,
        bool isMinimized = false,
        bool isMaximized = false,
        bool isForeground = false,
        bool isCloaked = false,
        int width = 800,
        int height = 600)
    {
        return new RawWindowInfo
        {
            Hwnd = hwnd,
            Title = title,
            ClassName = className,
            ProcessId = 1234,
            ProcessName = processName,
            CurrentRect = new Rect(0, 0, width, height),
            IsVisible = isVisible,
            IsMinimized = isMinimized,
            IsMaximized = isMaximized,
            IsForeground = isForeground,
            IsCloaked = isCloaked
        };
    }

    private WindowDiscoveryService CreateService(params IReadOnlyList<RawWindowInfo>[] snapshots)
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

    [Fact]
    public void Refresh_AddedWindow_ReportedInDiff()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe")
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe"),
            MakeRaw((IntPtr)200, title: "VS Code", processName: "Code.exe")
        };

        var service = CreateService(snapshot1, snapshot2);

        service.Refresh(_classifier);
        var diff = service.Refresh(_classifier);

        Assert.Single(diff.Added);
        Assert.Equal((IntPtr)200, diff.Added[0].Hwnd);
    }

    [Fact]
    public void Refresh_RemovedWindow_ReportedInDiff()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe"),
            MakeRaw((IntPtr)200, title: "VS Code", processName: "Code.exe")
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe")
        };

        var service = CreateService(snapshot1, snapshot2);

        service.Refresh(_classifier);
        var diff = service.Refresh(_classifier);

        Assert.Single(diff.Removed);
        Assert.Equal((IntPtr)200, diff.Removed[0].Hwnd);
    }

    [Fact]
    public void Refresh_ExistingWindow_InCurrent()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe"),
            MakeRaw((IntPtr)200, title: "VS Code", processName: "Code.exe")
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe"),
            MakeRaw((IntPtr)200, title: "VS Code", processName: "Code.exe")
        };

        var service = CreateService(snapshot1, snapshot2);

        service.Refresh(_classifier);
        var diff = service.Refresh(_classifier);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Equal(2, diff.Current.Count);
    }

    [Fact]
    public void Refresh_ExistingWindow_PreservesFirstSeenAt()
    {
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe")
        };

        var service = CreateService(snapshot, snapshot);
        var before = service.Refresh(_classifier);
        var firstSeen = before.Current[0].FirstSeenAt;

        var after = service.Refresh(_classifier);
        Assert.Equal(firstSeen, after.Current[0].FirstSeenAt);
    }

    [Fact]
    public void Refresh_ExistingWindow_UpdatesLastSeenAt()
    {
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe")
        };

        var service = CreateService(snapshot, snapshot);
        var first = service.Refresh(_classifier);
        var lastSeen1 = first.Current[0].LastSeenAt;

        var second = service.Refresh(_classifier);
        var lastSeen2 = second.Current[0].LastSeenAt;

        Assert.True(lastSeen2 >= lastSeen1);
    }

    [Fact]
    public void Refresh_ForegroundWindow_UpdatesLastFocusedAt()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe", isForeground: false)
        };

        var snapshot2 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe", isForeground: true)
        };

        var service = CreateService(snapshot1, snapshot2);
        service.Refresh(_classifier);

        var diff = service.Refresh(_classifier);
        Assert.NotNull(diff.Current[0].LastFocusedAt);
    }

    [Fact]
    public void Refresh_IneligibleWindows_Ignored()
    {
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "", processName: "chrome.exe"),
            MakeRaw((IntPtr)200, title: "Chrome", processName: "chrome.exe")
        };

        var service = CreateService(snapshot);
        var diff = service.Refresh(_classifier);

        Assert.Single(diff.Current);
        Assert.Equal((IntPtr)200, diff.Current[0].Hwnd);
    }

    [Fact]
    public void Refresh_FirstCall_AllAreAdded()
    {
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe"),
            MakeRaw((IntPtr)200, title: "VS Code", processName: "Code.exe")
        };

        var service = CreateService(snapshot);
        var diff = service.Refresh(_classifier);

        Assert.Equal(2, diff.Added.Count);
        Assert.Empty(diff.Removed);
        Assert.Equal(2, diff.Current.Count);
    }

    [Fact]
    public void Refresh_EmptySnapshot_AllRemoved()
    {
        var snapshot1 = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe")
        };

        var snapshot2 = new List<RawWindowInfo>();

        var service = CreateService(snapshot1, snapshot2);
        service.Refresh(_classifier);

        var diff = service.Refresh(_classifier);

        Assert.Empty(diff.Added);
        Assert.Single(diff.Removed);
        Assert.Empty(diff.Current);
    }

    [Fact]
    public void GetKnownWindows_ReturnsCurrentState()
    {
        var snapshot = new List<RawWindowInfo>
        {
            MakeRaw((IntPtr)100, title: "Chrome", processName: "chrome.exe"),
            MakeRaw((IntPtr)200, title: "VS Code", processName: "Code.exe")
        };

        var service = CreateService(snapshot);
        service.Refresh(_classifier);

        var known = service.GetKnownWindows();
        Assert.Equal(2, known.Count);
    }
}
