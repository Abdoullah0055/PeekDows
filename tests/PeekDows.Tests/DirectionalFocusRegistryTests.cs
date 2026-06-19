using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class DirectionalFocusRegistryTests
{
    private readonly DirectionalFocusRegistry _registry = new(_ => true, _ => true);

    private class FakeMonitorResolver : IMonitorResolver
    {
        private readonly Dictionary<IntPtr, MonitorInfo> _map;

        public FakeMonitorResolver(Dictionary<IntPtr, MonitorInfo> map)
        {
            _map = map;
        }

        public MonitorInfo GetMonitorForWindow(IntPtr hwnd)
        {
            return _map.TryGetValue(hwnd, out var info) ? info : _map[IntPtr.Zero];
        }

        public MonitorInfo GetPrimaryMonitor()
        {
            return _map[IntPtr.Zero];
        }
    }

    [Fact]
    public void MapSlotId_A_MapsToTopLeft()
    {
        Assert.Equal(DirectionalFocusSlot.TopLeft, DirectionalFocusRegistry.MapSlotId("A"));
    }

    [Fact]
    public void MapSlotId_B_MapsToBottomRight()
    {
        Assert.Equal(DirectionalFocusSlot.BottomRight, DirectionalFocusRegistry.MapSlotId("B"));
    }

    [Fact]
    public void MapSlotId_C_MapsToTopRight()
    {
        Assert.Equal(DirectionalFocusSlot.TopRight, DirectionalFocusRegistry.MapSlotId("C"));
    }

    [Fact]
    public void MapSlotId_D_MapsToBottomLeft()
    {
        Assert.Equal(DirectionalFocusSlot.BottomLeft, DirectionalFocusRegistry.MapSlotId("D"));
    }

    [Fact]
    public void MapSlotId_Unknown_ReturnsNull()
    {
        Assert.Null(DirectionalFocusRegistry.MapSlotId("FocusLarge"));
        Assert.Null(DirectionalFocusRegistry.MapSlotId("Focus"));
        Assert.Null(DirectionalFocusRegistry.MapSlotId("Unknown"));
    }

    [Fact]
    public void ReturnsSlotForCurrentMonitor()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) },
            new() { Hwnd = (IntPtr)101, SlotId = "B", TargetRect = new Rect(192, 108, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        var hwnd = _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft);
        Assert.Equal((IntPtr)100, hwnd);

        var hwndB = _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.BottomRight);
        Assert.Equal((IntPtr)101, hwndB);
    }

    [Fact]
    public void DoesNotReturnSlotFromOtherMonitor()
    {
        var monitor1WorkArea = new Rect(0, 0, 1920, 1080);
        var monitor2WorkArea = new Rect(1920, 0, 1920, 1080);

        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = monitor1WorkArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = monitor1WorkArea, IsPrimary = true },
            [(IntPtr)200] = new MonitorInfo { WorkArea = monitor2WorkArea, IsPrimary = false }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) },
            new() { Hwnd = (IntPtr)200, SlotId = "A", TargetRect = new Rect(1920, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        var hwnd = _registry.GetHwndForSlot(monitor2WorkArea, DirectionalFocusSlot.TopLeft);
        Assert.Equal((IntPtr)200, hwnd);

        var hwndM1 = _registry.GetHwndForSlot(monitor1WorkArea, DirectionalFocusSlot.TopLeft);
        Assert.Equal((IntPtr)100, hwndM1);
    }

    [Fact]
    public void ReturnsNullForEmptySlot()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        var hwnd = _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopRight);
        Assert.Null(hwnd);
    }

    [Fact]
    public void RemovesInvalidWindowHandle()
    {
        var strictRegistry = new DirectionalFocusRegistry(hwnd => hwnd != (IntPtr)999, _ => true);
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)999] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)999, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        strictRegistry.UpdateFromPlacements(placements, monitorResolver);

        var hwnd = strictRegistry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft);
        Assert.Null(hwnd);
    }

    [Fact]
    public void UpdatesSlotsAfterArrange()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)101] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var firstPlacements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(firstPlacements, monitorResolver);
        Assert.Equal((IntPtr)100, _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));

        var secondPlacements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)101, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(secondPlacements, monitorResolver);
        Assert.Equal((IntPtr)101, _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void HasSlotsForMonitor_ReturnsFalse_WhenNoPlacements()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        Assert.False(_registry.HasSlotsForMonitor(workArea));
    }

    [Fact]
    public void HasSlotsForMonitor_ReturnsTrue_AfterUpdate()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);
        Assert.True(_registry.HasSlotsForMonitor(workArea));
    }

    [Fact]
    public void IgnoresNonSlotPlacements()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "FocusLarge", TargetRect = workArea }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);
        Assert.False(_registry.HasSlotsForMonitor(workArea));
    }

    [Fact]
    public void IgnoresPlacementNotOnCurrentVirtualDesktop()
    {
        var registry = new DirectionalFocusRegistry(_ => true, hwnd => hwnd != (IntPtr)300);
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)300] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) },
            new() { Hwnd = (IntPtr)300, SlotId = "B", TargetRect = new Rect(192, 108, 1728, 972) }
        };

        registry.UpdateFromPlacements(placements, monitorResolver);

        Assert.Equal((IntPtr)100, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));
        Assert.Null(registry.GetHwndForSlot(workArea, DirectionalFocusSlot.BottomRight));
    }

    [Fact]
    public void DoesNotRemoveSlotWhenWindowNotOnCurrentVirtualDesktop_KeepsSlotForReturn()
    {
        // After a virtual-desktop switch, the slot must NOT be purged: returning to the
        // original desktop must make the window focusable again without re-arranging.
        bool hwnd200OnDesktop = true;
        var registry = new DirectionalFocusRegistry(_ => true, hwnd => hwnd != (IntPtr)200 || hwnd200OnDesktop);
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)200] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)200, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        registry.UpdateFromPlacements(placements, monitorResolver);
        Assert.Equal((IntPtr)200, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));

        // Switch away: lookup returns null but the slot is preserved.
        hwnd200OnDesktop = false;
        Assert.Null(registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));

        // Switch back: the slot resolves again without any re-arrange.
        hwnd200OnDesktop = true;
        Assert.Equal((IntPtr)200, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void SetSlotsForMonitor_ReplacesSlotsForMonitor()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var workArea = new Rect(0, 0, 1920, 1080);

        registry.SetSlotsForMonitor(workArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)100,
            [DirectionalFocusSlot.BottomRight] = (IntPtr)101
        });

        Assert.Equal((IntPtr)100, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));
        Assert.Equal((IntPtr)101, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.BottomRight));
        Assert.Null(registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopRight));
        Assert.True(registry.HasSlotsForMonitor(workArea));
    }

    [Fact]
    public void SetSlotsForMonitor_OverwritesPreviousSlotsForSameMonitor()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var workArea = new Rect(0, 0, 1920, 1080);

        registry.SetSlotsForMonitor(workArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)100
        });

        registry.SetSlotsForMonitor(workArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopRight] = (IntPtr)200
        });

        Assert.Null(registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));
        Assert.Equal((IntPtr)200, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopRight));
    }

    [Fact]
    public void SetSlotsForMonitor_DoesNotAffectOtherMonitor()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var monitor1 = new Rect(0, 0, 1920, 1080);
        var monitor2 = new Rect(1920, 0, 1920, 1080);

        registry.SetSlotsForMonitor(monitor1, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)100
        });
        registry.SetSlotsForMonitor(monitor2, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)200
        });

        Assert.Equal((IntPtr)100, registry.GetHwndForSlot(monitor1, DirectionalFocusSlot.TopLeft));
        Assert.Equal((IntPtr)200, registry.GetHwndForSlot(monitor2, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void ReturnsNullForWindowOnOtherVirtualDesktop()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => false);
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        registry.UpdateFromPlacements(placements, monitorResolver);
        Assert.Null(registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void ReturnsSlotWhenWindowIsOnCurrentVirtualDesktop()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        registry.UpdateFromPlacements(placements, monitorResolver);
        Assert.Equal((IntPtr)100, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopLeft));
    }
}
