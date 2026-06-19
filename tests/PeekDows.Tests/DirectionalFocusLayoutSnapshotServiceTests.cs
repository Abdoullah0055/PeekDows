using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class DirectionalFocusLayoutSnapshotServiceTests
{
    private static readonly Rect WorkArea = new(0, 0, 1920, 1080);

    private static Rect ExpectedSlotRect(string slotId)
        => new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea)[slotId];

    private static ManagedWindow Window(IntPtr hwnd, Rect rect, string title = "App")
        => new()
        {
            Hwnd = hwnd,
            Title = title,
            ClassName = "Class",
            ProcessName = "app.exe",
            CurrentRect = rect,
            IsVisible = true
        };

    private sealed class SingleMonitorResolver : IMonitorResolver
    {
        private readonly Rect _workArea;
        public SingleMonitorResolver(Rect workArea) { _workArea = workArea; }
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd) => new() { WorkArea = _workArea, IsPrimary = true };
        public MonitorInfo GetPrimaryMonitor() => new() { WorkArea = _workArea, IsPrimary = true };
    }

    private static DirectionalFocusLayoutSnapshotService CreateService(
        Func<IntPtr, Rect> rectFor,
        Func<IntPtr, bool>? isOnCurrentDesktop = null)
    {
        return new DirectionalFocusLayoutSnapshotService(
            new SingleMonitorResolver(WorkArea),
            new LayoutEngine(),
            rectFor,
            isOnCurrentDesktop ?? (_ => true));
    }

    [Fact]
    public void MatchesTopLeftSlot()
    {
        var rectA = ExpectedSlotRect("A");
        var service = CreateService(hwnd => hwnd == (IntPtr)10 ? rectA : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)10 });

        Assert.Equal((IntPtr)10, map[DirectionalFocusSlot.TopLeft]);
    }

    [Fact]
    public void MatchesBottomRightSlot()
    {
        var rectB = ExpectedSlotRect("B");
        var service = CreateService(hwnd => hwnd == (IntPtr)11 ? rectB : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)11 });

        Assert.Equal((IntPtr)11, map[DirectionalFocusSlot.BottomRight]);
    }

    [Fact]
    public void MatchesTopRightSlot()
    {
        var rectC = ExpectedSlotRect("C");
        var service = CreateService(hwnd => hwnd == (IntPtr)12 ? rectC : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)12 });

        Assert.Equal((IntPtr)12, map[DirectionalFocusSlot.TopRight]);
    }

    [Fact]
    public void MatchesBottomLeftSlot()
    {
        var rectD = ExpectedSlotRect("D");
        var service = CreateService(hwnd => hwnd == (IntPtr)13 ? rectD : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)13 });

        Assert.Equal((IntPtr)13, map[DirectionalFocusSlot.BottomLeft]);
    }

    [Fact]
    public void ReturnsEmpty_WhenRectsDoNotMatchPeekDowsLayout()
    {
        // Window sitting at an arbitrary non-layout position.
        var service = CreateService(_ => new Rect(317, 244, 800, 600));

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)20 });

        Assert.Empty(map);
    }

    [Fact]
    public void UsesTolerance_MatchesWithin40Pixels()
    {
        var rectA = ExpectedSlotRect("A");
        // Perturb by ±25px on every edge — must still match.
        var drifted = new Rect(
            rectA.Left + 25,
            rectA.Top - 18,
            rectA.Width + 12,
            rectA.Height - 30);

        var service = CreateService(hwnd => hwnd == (IntPtr)30 ? drifted : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)30 });

        Assert.True(map.ContainsKey(DirectionalFocusSlot.TopLeft));
        Assert.Equal((IntPtr)30, map[DirectionalFocusSlot.TopLeft]);
    }

    [Fact]
    public void UsesTolerance_RejectsBeyondTolerance()
    {
        var rectA = ExpectedSlotRect("A");
        // Shrink the size by 500px — far beyond the 40px tolerance. (Drifting only the
        // Left/Top would land on another overlapping slot, since the four ClassicPeekGrid
        // slots share the same Width/Height and overlap by 90%.)
        var drifted = new Rect(rectA.Left, rectA.Top, rectA.Width - 500, rectA.Height - 500);

        var service = CreateService(hwnd => hwnd == (IntPtr)31 ? drifted : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)31 });

        Assert.Empty(map);
    }

    [Fact]
    public void DoesNotMapWindowFromOtherMonitor()
    {
        var otherWorkArea = new Rect(1920, 0, 1920, 1080);
        var rectAOnOther = new LayoutEngine().CalculateClassicPeekGridSlotRects(otherWorkArea)["A"];

        var rectFor = (IntPtr hwnd) => hwnd == (IntPtr)40 ? rectAOnOther : default;
        var monitorFor = new SingleMonitorResolver(otherWorkArea);

        var service = new DirectionalFocusLayoutSnapshotService(
            monitorFor,
            new LayoutEngine(),
            rectFor,
            _ => true);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)40 });

        Assert.Empty(map);
    }

    [Fact]
    public void HandlesTwoWindowLayout_AAndBOnly()
    {
        var slotRects = new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea);
        var rectFor = (IntPtr hwnd) => hwnd switch
        {
            IntPtr h when h == (IntPtr)50 => slotRects["A"],
            IntPtr h when h == (IntPtr)51 => slotRects["B"],
            _ => default
        };

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)50, (IntPtr)51 });

        Assert.Equal((IntPtr)50, map[DirectionalFocusSlot.TopLeft]);
        Assert.Equal((IntPtr)51, map[DirectionalFocusSlot.BottomRight]);
        Assert.False(map.ContainsKey(DirectionalFocusSlot.TopRight));
        Assert.False(map.ContainsKey(DirectionalFocusSlot.BottomLeft));
    }

    [Fact]
    public void HandlesFourWindowLayout_AllSlots()
    {
        var slotRects = new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea);
        var rectFor = (IntPtr hwnd) => hwnd switch
        {
            IntPtr h when h == (IntPtr)60 => slotRects["A"],
            IntPtr h when h == (IntPtr)61 => slotRects["B"],
            IntPtr h when h == (IntPtr)62 => slotRects["C"],
            IntPtr h when h == (IntPtr)63 => slotRects["D"],
            _ => default
        };

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)60, (IntPtr)61, (IntPtr)62, (IntPtr)63 });

        Assert.Equal((IntPtr)60, map[DirectionalFocusSlot.TopLeft]);
        Assert.Equal((IntPtr)61, map[DirectionalFocusSlot.BottomRight]);
        Assert.Equal((IntPtr)62, map[DirectionalFocusSlot.TopRight]);
        Assert.Equal((IntPtr)63, map[DirectionalFocusSlot.BottomLeft]);
    }

    [Fact]
    public void HandlesThreeWindowLayout_A_B_C_Only()
    {
        var slotRects = new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea);
        var rectFor = (IntPtr hwnd) => hwnd switch
        {
            IntPtr h when h == (IntPtr)70 => slotRects["A"],
            IntPtr h when h == (IntPtr)71 => slotRects["B"],
            IntPtr h when h == (IntPtr)72 => slotRects["C"],
            _ => default
        };

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)70, (IntPtr)71, (IntPtr)72 });

        Assert.Equal((IntPtr)70, map[DirectionalFocusSlot.TopLeft]);
        Assert.Equal((IntPtr)71, map[DirectionalFocusSlot.BottomRight]);
        Assert.Equal((IntPtr)72, map[DirectionalFocusSlot.TopRight]);
        Assert.False(map.ContainsKey(DirectionalFocusSlot.BottomLeft));
    }

    [Fact]
    public void DoesNotMapWindowNotOnCurrentVirtualDesktop()
    {
        var rectA = ExpectedSlotRect("A");
        var service = CreateService(hwnd => hwnd == (IntPtr)80 ? rectA : default, hwnd => hwnd != (IntPtr)80);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)80 });

        Assert.Empty(map);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var service = CreateService(_ => default);
        var map = service.BuildSlotMap(WorkArea, Array.Empty<IntPtr>());
        Assert.Empty(map);
    }

    [Fact]
    public void FirstMatchingWindowWins_WhenTwoWindowsOverlapSameSlot()
    {
        var rectA = ExpectedSlotRect("A");
        var rectFor = (IntPtr hwnd) => hwnd == (IntPtr)90 || hwnd == (IntPtr)91 ? rectA : default;

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)90, (IntPtr)91 });

        Assert.Equal((IntPtr)90, map[DirectionalFocusSlot.TopLeft]);
    }
}
