using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class WindowPlacementServiceTests
{
    private readonly FakeWindowPositioner _fake;
    private readonly WindowPlacementService _service;

    public WindowPlacementServiceTests()
    {
        _fake = new FakeWindowPositioner();
        _service = new WindowPlacementService(_fake);
    }

    [Fact]
    public void ApplyPlacements_EmptyList_ReturnsZeroCounts()
    {
        var result = _service.ApplyPlacements([]);

        Assert.Equal(0, result.AttemptedCount);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public void ApplyPlacements_NullList_ReturnsZeroCounts()
    {
        var result = _service.ApplyPlacements(null!);

        Assert.Equal(0, result.AttemptedCount);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public void ApplyPlacements_ValidPlacements_CallsPositioner()
    {
        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 960, 540) },
            new() { Hwnd = (IntPtr)200, SlotId = "B", TargetRect = new Rect(480, 270, 960, 540) }
        };

        var result = _service.ApplyPlacements(placements);

        Assert.Equal(2, result.AttemptedCount);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(2, _fake.Calls.Count);
    }

    [Fact]
    public void ApplyPlacements_WhenOneFails_ContinuesWithOthers()
    {
        var failHwnds = new[] { (IntPtr)200 };
        var fake = new FakeWindowPositioner(failHwnds);
        var service = new WindowPlacementService(fake);

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 960, 540) },
            new() { Hwnd = (IntPtr)200, SlotId = "B", TargetRect = new Rect(480, 270, 960, 540) },
            new() { Hwnd = (IntPtr)300, SlotId = "C", TargetRect = new Rect(480, 0, 960, 540) }
        };

        var result = service.ApplyPlacements(placements);

        Assert.Equal(3, result.AttemptedCount);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public void ApplyPlacements_UsesTargetRect()
    {
        var targetRect = new Rect(100, 200, 800, 600);
        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)1, SlotId = "A", TargetRect = targetRect }
        };

        _service.ApplyPlacements(placements);

        Assert.Single(_fake.Calls);
        Assert.Equal(targetRect, _fake.Calls[0].Rect);
    }

    [Fact]
    public void ApplyPlacements_ZeroHwnd_ReportsFailure()
    {
        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = IntPtr.Zero, SlotId = "A", TargetRect = new Rect(0, 0, 960, 540) }
        };

        var result = _service.ApplyPlacements(placements);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SucceededCount);
    }

    [Fact]
    public void ApplyPlacements_InvalidRect_ReportsFailure()
    {
        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)1, SlotId = "A", TargetRect = new Rect(0, 0, 0, 0) }
        };

        var result = _service.ApplyPlacements(placements);

        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public void ApplyPlacements_CapturesErrorMessages()
    {
        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = IntPtr.Zero, SlotId = "A", TargetRect = new Rect(0, 0, 960, 540) }
        };

        var result = _service.ApplyPlacements(placements);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ApplyPlacements_InvalidWindow_ReportsFailure()
    {
        var fake = new FakeWindowPositioner();
        var service = new WindowPlacementService(fake, _ => false);

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)1, SlotId = "A", TargetRect = new Rect(0, 0, 960, 540) }
        };

        var result = service.ApplyPlacements(placements);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SucceededCount);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void ApplyPlacements_MaximizedWindow_RestoresBeforeSetWindowPos()
    {
        var fake = new FakeWindowPositioner();
        var restored = new List<IntPtr>();
        Func<IntPtr, bool> isMaximized = hwnd => { restored.Add(hwnd); return hwnd == (IntPtr)100; };
        var service = new WindowPlacementService(fake, _ => true, isMaximized, null);

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 640, 360) }
        };

        var result = service.ApplyPlacements(placements);

        Assert.Equal(1, result.SucceededCount);
        Assert.Contains((IntPtr)100, restored);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public void ApplyPlacements_NormalWindow_DoesNotCallRestore()
    {
        var fake = new FakeWindowPositioner();
        var restoreChecks = new List<IntPtr>();
        Func<IntPtr, bool> isMaximized = hwnd => { restoreChecks.Add(hwnd); return false; };
        var service = new WindowPlacementService(fake, _ => true, isMaximized, null);

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 640, 360) }
        };

        var result = service.ApplyPlacements(placements);

        Assert.Equal(1, result.SucceededCount);
        Assert.Single(restoreChecks);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public void ApplyPlacements_RestoreFailure_DoesNotStopSetWindowPos()
    {
        var fake = new FakeWindowPositioner();
        Func<IntPtr, bool> isMaximized = hwnd => true;
        var service = new WindowPlacementService(fake, _ => true, isMaximized, null);

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 640, 360) }
        };

        var result = service.ApplyPlacements(placements);

        Assert.Equal(1, result.SucceededCount);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public void ApplyPlacements_FocusPlacement_PassesBringToFrontTrue()
    {
        var fake = new FakeWindowPositioner();
        var service = new WindowPlacementService(fake, _ => true, _ => false, null);

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "Focus", TargetRect = new Rect(0, 0, 1120, 600), BringToFront = true }
        };

        service.ApplyPlacements(placements);

        Assert.Single(fake.Calls);
        Assert.True(fake.Calls[0].BringToFront);
    }

    [Fact]
    public void ApplyPlacements_PeekPlacement_PassesBringToFrontFalse()
    {
        var fake = new FakeWindowPositioner();
        var service = new WindowPlacementService(fake, _ => true, _ => false, null);

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "BottomRight", TargetRect = new Rect(200, 260, 860, 460), BringToFront = false }
        };

        service.ApplyPlacements(placements);

        Assert.Single(fake.Calls);
        Assert.False(fake.Calls[0].BringToFront);
    }
}
