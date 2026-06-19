using System;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

/// <summary>
/// Tests for <see cref="DirectionalFocusLayoutSnapshotService.IsWindowStillInSlot"/>,
/// which validates that a window physically still occupies its expected PeekDows slot
/// before Directional Focus trusts the registry fast path.
/// </summary>
public class DirectionalFocusLayoutSnapshotServiceIsWindowStillInSlotTests
{
    private static readonly Rect WorkArea = new(0, 0, 1920, 1080);
    private static readonly Rect OtherWorkArea = new(1920, 0, 1920, 1080);

    private static Rect ExpectedSlotRect(string slotId, Rect workArea)
        => new LayoutEngine().CalculateClassicPeekGridSlotRects(workArea)[slotId];

    private sealed class SingleMonitorResolver : IMonitorResolver
    {
        private readonly Rect _workArea;
        public SingleMonitorResolver(Rect workArea) { _workArea = workArea; }
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd) => new() { WorkArea = _workArea, IsPrimary = true };
        public MonitorInfo GetPrimaryMonitor() => new() { WorkArea = _workArea, IsPrimary = true };
    }

    private static DirectionalFocusLayoutSnapshotService CreateService(
        Func<IntPtr, Rect> rectFor,
        Func<IntPtr, bool>? isOnCurrentDesktop = null,
        IMonitorResolver? monitorResolver = null)
    {
        return new DirectionalFocusLayoutSnapshotService(
            monitorResolver ?? new SingleMonitorResolver(WorkArea),
            new LayoutEngine(),
            rectFor,
            isOnCurrentDesktop ?? (_ => true));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsTrue_WhenWindowMatchesTopLeft()
    {
        var rectA = ExpectedSlotRect("A", WorkArea);
        var service = CreateService(hwnd => hwnd == (IntPtr)10 ? rectA : default);

        Assert.True(service.IsWindowStillInSlot((IntPtr)10, WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsTrue_WhenWindowMatchesBottomRight()
    {
        var rectB = ExpectedSlotRect("B", WorkArea);
        var service = CreateService(hwnd => hwnd == (IntPtr)11 ? rectB : default);

        Assert.True(service.IsWindowStillInSlot((IntPtr)11, WorkArea, DirectionalFocusSlot.BottomRight));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsTrue_WhenWindowMatchesTopRight()
    {
        var rectC = ExpectedSlotRect("C", WorkArea);
        var service = CreateService(hwnd => hwnd == (IntPtr)12 ? rectC : default);

        Assert.True(service.IsWindowStillInSlot((IntPtr)12, WorkArea, DirectionalFocusSlot.TopRight));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsTrue_WhenWindowMatchesBottomLeft()
    {
        var rectD = ExpectedSlotRect("D", WorkArea);
        var service = CreateService(hwnd => hwnd == (IntPtr)13 ? rectD : default);

        Assert.True(service.IsWindowStillInSlot((IntPtr)13, WorkArea, DirectionalFocusSlot.BottomLeft));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsFalse_WhenWindowMovedAwayFromSlot()
    {
        var rectA = ExpectedSlotRect("A", WorkArea);
        // User dragged the window to an arbitrary position far from slot A.
        var movedAway = new Rect(317, 244, rectA.Width - 500, rectA.Height - 500);
        var service = CreateService(hwnd => hwnd == (IntPtr)20 ? movedAway : default);

        Assert.False(service.IsWindowStillInSlot((IntPtr)20, WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsFalse_WhenWindowOnOtherMonitor()
    {
        var rectAOnOther = ExpectedSlotRect("A", OtherWorkArea);
        // The resolver reports the window on the *other* monitor even though we ask about WorkArea.
        var otherMonitorResolver = new SingleMonitorResolver(OtherWorkArea);
        var service = CreateService(hwnd => hwnd == (IntPtr)30 ? rectAOnOther : default, monitorResolver: otherMonitorResolver);

        Assert.False(service.IsWindowStillInSlot((IntPtr)30, WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsFalse_WhenWindowNotOnCurrentVirtualDesktop()
    {
        var rectA = ExpectedSlotRect("A", WorkArea);
        var service = CreateService(
            hwnd => hwnd == (IntPtr)40 ? rectA : default,
            hwnd => hwnd != (IntPtr)40); // 40 is on another desktop

        Assert.False(service.IsWindowStillInSlot((IntPtr)40, WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsFalse_ForZeroHwnd()
    {
        var service = CreateService(_ => default);
        Assert.False(service.IsWindowStillInSlot(IntPtr.Zero, WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void IsWindowStillInSlot_UsesTolerance_MatchesWithinTolerance()
    {
        var rectA = ExpectedSlotRect("A", WorkArea);
        var drifted = new Rect(rectA.Left + 25, rectA.Top - 18, rectA.Width + 12, rectA.Height - 30);
        var service = CreateService(hwnd => hwnd == (IntPtr)50 ? drifted : default);

        Assert.True(service.IsWindowStillInSlot((IntPtr)50, WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void IsWindowStillInSlot_ReturnsFalse_WhenBeyondTolerance()
    {
        var rectA = ExpectedSlotRect("A", WorkArea);
        // Shrink size by 500px — far beyond the 40px tolerance (size change can't match
        // another overlapping slot the way a Left/Top drift could).
        var drifted = new Rect(rectA.Left, rectA.Top, rectA.Width - 500, rectA.Height - 500);
        var service = CreateService(hwnd => hwnd == (IntPtr)51 ? drifted : default);

        Assert.False(service.IsWindowStillInSlot((IntPtr)51, WorkArea, DirectionalFocusSlot.TopLeft));
    }
}
