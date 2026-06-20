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
    public void MapSlotId_E_MapsToTopCenter()
    {
        Assert.Equal(DirectionalFocusSlot.TopCenter, DirectionalFocusRegistry.MapSlotId("E"));
    }

    [Fact]
    public void MapSlotId_F_MapsToBottomCenter()
    {
        Assert.Equal(DirectionalFocusSlot.BottomCenter, DirectionalFocusRegistry.MapSlotId("F"));
    }

    [Fact]
    public void MapSlotId_G_MapsToMiddleRight()
    {
        Assert.Equal(DirectionalFocusSlot.MiddleRight, DirectionalFocusRegistry.MapSlotId("G"));
    }

    [Fact]
    public void MapSlotId_H_MapsToMiddleLeft()
    {
        Assert.Equal(DirectionalFocusSlot.MiddleLeft, DirectionalFocusRegistry.MapSlotId("H"));
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

    // --- Edge-centred slots E/F/G/H: registry storage, multi-monitor isolation, and
    // virtual-desktop filtering must work exactly like the corner slots A/B/C/D. ---

    [Fact]
    public void EdgeSlot_E_IsStoredAndResolved()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)150] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)150, SlotId = "E", TargetRect = new Rect(96, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        Assert.Equal((IntPtr)150, _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopCenter));
    }

    [Fact]
    public void EdgeSlot_F_G_H_AreStoredAndResolved()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)151] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)152] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)153] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)151, SlotId = "F", TargetRect = new Rect(96, 108, 1728, 972) },
            new() { Hwnd = (IntPtr)152, SlotId = "G", TargetRect = new Rect(192, 54, 1728, 972) },
            new() { Hwnd = (IntPtr)153, SlotId = "H", TargetRect = new Rect(0, 54, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        Assert.Equal((IntPtr)151, _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.BottomCenter));
        Assert.Equal((IntPtr)152, _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.MiddleRight));
        Assert.Equal((IntPtr)153, _registry.GetHwndForSlot(workArea, DirectionalFocusSlot.MiddleLeft));
    }

    [Fact]
    public void EdgeSlot_E_OnWrongMonitor_IsIgnored()
    {
        // A Ctrl+Shift+up gesture resolves slot E for the monitor under the cursor. A window
        // sitting in slot E on a *different* monitor must never be returned.
        var monitor1WorkArea = new Rect(0, 0, 1920, 1080);
        var monitor2WorkArea = new Rect(1920, 0, 1920, 1080);

        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = monitor1WorkArea, IsPrimary = true },
            [(IntPtr)160] = new MonitorInfo { WorkArea = monitor2WorkArea, IsPrimary = false }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)160, SlotId = "E", TargetRect = new Rect(2016, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        // Slot E is only populated on monitor 2; asking monitor 1 for it yields nothing.
        Assert.Null(_registry.GetHwndForSlot(monitor1WorkArea, DirectionalFocusSlot.TopCenter));
        Assert.Equal((IntPtr)160, _registry.GetHwndForSlot(monitor2WorkArea, DirectionalFocusSlot.TopCenter));
    }

    [Fact]
    public void EdgeSlot_E_OnOtherVirtualDesktop_ReturnsNullButKeepsSlot()
    {
        // Same rule as A/B/C/D: a slot pointing at a window on another virtual desktop is
        // not resolvable now, but is preserved so it works again when the user returns.
        bool hwnd170OnDesktop = true;
        var registry = new DirectionalFocusRegistry(_ => true, hwnd => hwnd != (IntPtr)170 || hwnd170OnDesktop);
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)170] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)170, SlotId = "E", TargetRect = new Rect(96, 0, 1728, 972) }
        };

        registry.UpdateFromPlacements(placements, monitorResolver);
        Assert.Equal((IntPtr)170, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopCenter));

        hwnd170OnDesktop = false;
        Assert.Null(registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopCenter));

        hwnd170OnDesktop = true;
        Assert.Equal((IntPtr)170, registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopCenter));
    }

    [Fact]
    public void EdgeSlot_Absent_ReturnsNull()
    {
        // Ctrl+Shift+up when no window is in slot E on this monitor must be a silent no-op.
        var workArea = new Rect(0, 0, 1920, 1080);
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = new MonitorInfo { WorkArea = workArea, IsPrimary = true },
            [(IntPtr)100] = new MonitorInfo { WorkArea = workArea, IsPrimary = true }
        });

        // Only slot A is populated; E/F/G/H are all empty.
        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)100, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        Assert.Null(_registry.GetHwndForSlot(workArea, DirectionalFocusSlot.TopCenter));
        Assert.Null(_registry.GetHwndForSlot(workArea, DirectionalFocusSlot.BottomCenter));
        Assert.Null(_registry.GetHwndForSlot(workArea, DirectionalFocusSlot.MiddleRight));
        Assert.Null(_registry.GetHwndForSlot(workArea, DirectionalFocusSlot.MiddleLeft));
    }

    // --- Hardening: a window whose monitor cannot be resolved must not be mapped into a
    // synthetic fallback monitor bucket used for focus decisions. ---

    [Fact]
    public void FallbackMonitor_PlacementIsNotMapped()
    {
        // The resolver returns a fallback MonitorInfo (as MonitorService does when
        // GetMonitorInfo fails). The registry must skip the placement entirely rather than
        // key it under the synthetic 1920x1080 work area.
        var fallbackMonitor = new MonitorInfo
        {
            WorkArea = new Rect(0, 0, 1920, 1080),
            FullArea = new Rect(0, 0, 1920, 1080),
            IsPrimary = true,
            IsFallback = true
        };
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = fallbackMonitor,
            [(IntPtr)200] = fallbackMonitor
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)200, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        // Nothing mapped: querying the fallback work area returns nothing, so Directional
        // Focus never targets a window via an invented monitor.
        Assert.Null(_registry.GetHwndForSlot(new Rect(0, 0, 1920, 1080), DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void RealMonitor_PlacementIsMapped_AndFallbackDoesNotCollide()
    {
        // A window on a real monitor is mapped normally; a separate fallback window does
        // not overwrite its slot (the old bug collapsed both into one fake bucket).
        var realMonitor = new MonitorInfo
        {
            WorkArea = new Rect(0, 0, 1920, 1080),
            FullArea = new Rect(0, 0, 1920, 1080),
            IsPrimary = true,
            IsFallback = false
        };
        var fallbackMonitor = new MonitorInfo
        {
            WorkArea = new Rect(0, 0, 1920, 1080),
            IsPrimary = true,
            IsFallback = true
        };
        var monitorResolver = new FakeMonitorResolver(new Dictionary<IntPtr, MonitorInfo>
        {
            [IntPtr.Zero] = realMonitor,
            [(IntPtr)201] = realMonitor,
            [(IntPtr)202] = fallbackMonitor
        });

        var placements = new List<WindowPlacement>
        {
            new() { Hwnd = (IntPtr)201, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) },
            new() { Hwnd = (IntPtr)202, SlotId = "A", TargetRect = new Rect(0, 0, 1728, 972) }
        };

        _registry.UpdateFromPlacements(placements, monitorResolver);

        // The real window keeps slot A; the fallback window was never mapped, so it cannot
        // have overwritten it.
        Assert.Equal((IntPtr)201, _registry.GetHwndForSlot(new Rect(0, 0, 1920, 1080), DirectionalFocusSlot.TopLeft));
    }
}
