using System;
using System.Collections.Generic;
using PeekDows.App.Focus;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

/// <summary>
/// Exercises the registry-miss → on-demand rebuild flow of
/// <see cref="DirectionalFocusService"/> without touching Win32. Uses the internal
/// <c>ResolveHwndForSlot</c> entry point which contains all of the lookup + rebuild
/// logic and is free of P/Invoke.
/// </summary>
public class DirectionalFocusServiceRebuildTests
{
    private static readonly Rect WorkArea = new(0, 0, 1920, 1080);

    private sealed class FakeMonitorResolver : IMonitorResolver
    {
        private readonly Rect _workArea;
        public FakeMonitorResolver(Rect workArea) { _workArea = workArea; }
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd) => new() { WorkArea = _workArea, IsPrimary = true };
        public MonitorInfo GetPrimaryMonitor() => new() { WorkArea = _workArea, IsPrimary = true };
    }

    private static DirectionalFocusService CreateService(
        DirectionalFocusRegistry registry,
        DirectionalFocusLayoutSnapshotService snapshot,
        Func<IReadOnlyList<IntPtr>> candidateSource)
    {
        return new DirectionalFocusService(
            new DirectionalFocusGestureDetector(),
            registry,
            new FakeMonitorResolver(WorkArea),
            new WindowActivationService(new AlwaysFailActivationApi()),
            new AlwaysTrueVirtualDesktopService(),
            snapshot,
            candidateSource,
            () => 80,
            logger: null);
    }

    private sealed class AlwaysFailActivationApi : IWindowActivationApi
    {
        public bool IsWindow(IntPtr hwnd) => true;
        public bool IsIconic(IntPtr hwnd) => false;
        public bool ShowWindow(IntPtr hwnd, int cmdShow) => true;
        public bool ShowWindowAsync(IntPtr hwnd, int cmdShow) => true;
        public IntPtr GetForegroundWindow() => IntPtr.Zero;
        public uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid) { pid = 0; return 1; }
        public uint GetCurrentThreadId() => 1;
        public bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach) => true;
        public bool BringWindowToTop(IntPtr hwnd) => true;
        public bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags) => true;
        public bool SetForegroundWindow(IntPtr hwnd) => true;
        public IntPtr SetFocus(IntPtr hwnd) => IntPtr.Zero;
        public int GetWindowText(IntPtr hwnd, System.Text.StringBuilder sb, int maxCount) => 0;
        public uint GetWindowProcessId(IntPtr hwnd) => 0;
        public bool IsResponsive(IntPtr hwnd, uint timeoutMs) => true;
        public bool TryUnlockForegroundWithAltPulse() => true;
    }

    private sealed class AlwaysTrueVirtualDesktopService : IVirtualDesktopService
    {
        public bool IsWindowOnCurrentVirtualDesktop(IntPtr hwnd) => true;
    }

    private static DirectionalFocusLayoutSnapshotService CreateSnapshot(Func<IntPtr, Rect> rectFor)
    {
        return new DirectionalFocusLayoutSnapshotService(
            new FakeMonitorResolver(WorkArea),
            new LayoutEngine(),
            rectFor,
            _ => true);
    }

    private static Rect SlotRect(string slotId)
        => new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea)[slotId];

    [Fact]
    public void WhenRegistryMisses_RebuildsFromCurrentLayout()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectA = SlotRect("A");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)10 ? rectA : default);

        var sourceCallCount = 0;
        IReadOnlyList<IntPtr> Source()
        {
            sourceCallCount++;
            return new[] { (IntPtr)10 };
        }

        var service = CreateService(registry, snapshot, Source);

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        Assert.Equal((IntPtr)10, hwnd);
        Assert.Equal(1, sourceCallCount);
        // After rebuild, the registry itself must hold the slot.
        Assert.Equal((IntPtr)10, registry.GetHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void WhenLayoutNotRecognized_DoesNotActivate()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        // Window at a non-layout position.
        var snapshot = CreateSnapshot(_ => new Rect(317, 244, 800, 600));

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)20 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        Assert.Null(hwnd);
        Assert.False(registry.HasSlotsForMonitor(WorkArea));
    }

    [Fact]
    public void WhenRebuildFindsSlot_ResolvesWindow()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectB = SlotRect("B");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)30 ? rectB : default);

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)30 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.BottomRight);

        Assert.Equal((IntPtr)30, hwnd);
    }

    [Fact]
    public void WhenRegistryAlreadyHasSlot_DoesNotRebuild()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        registry.SetSlotsForMonitor(WorkArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)40
        });

        // The window must still physically occupy slot A for the fast path to be trusted.
        var rectA = SlotRect("A");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)40 ? rectA : default);
        var sourceCallCount = 0;

        var service = CreateService(
            registry,
            snapshot,
            () => { sourceCallCount++; return Array.Empty<IntPtr>(); });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        Assert.Equal((IntPtr)40, hwnd);
        Assert.Equal(0, sourceCallCount);
    }

    [Fact]
    public void RebuildIsRateLimited()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectA = SlotRect("A");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)50 ? rectA : default);

        var sourceCallCount = 0;
        IReadOnlyList<IntPtr> Source()
        {
            sourceCallCount++;
            return new[] { (IntPtr)50 };
        }

        var service = CreateService(registry, snapshot, Source);

        // First call: miss → rebuild (count=1), slot now resolved.
        var first = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopRight);
        Assert.Null(first);
        Assert.Equal(1, sourceCallCount);

        // Immediately after: registry still misses TopRight, but the rebuild is
        // rate-limited so the candidate source must NOT be called again.
        var second = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopRight);
        Assert.Null(second);
        Assert.Equal(1, sourceCallCount);
    }

    [Fact]
    public void ResolveHwndForSlot_UsesRegistry_WhenWindowStillMatchesSlot()
    {
        // Registry maps TopLeft→60, and the window physically still occupies slot A.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        registry.SetSlotsForMonitor(WorkArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)60
        });

        var rectA = SlotRect("A");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)60 ? rectA : default);

        var sourceCallCount = 0;
        var service = CreateService(
            registry,
            snapshot,
            () => { sourceCallCount++; return Array.Empty<IntPtr>(); });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        // Fast path trusted (window still in slot), no rebuild.
        Assert.Equal((IntPtr)60, hwnd);
        Assert.Equal(0, sourceCallCount);
    }

    [Fact]
    public void ResolveHwndForSlot_Rebuilds_WhenRegistryWindowMovedAwayFromSlot()
    {
        // Registry maps TopLeft→70, but the user dragged window 70 away from slot A.
        // A *different* window (71) is now physically sitting in slot A.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        registry.SetSlotsForMonitor(WorkArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)70
        });

        var rectA = SlotRect("A");
        var movedAway = new Rect(317, 244, rectA.Width - 500, rectA.Height - 500);
        var snapshot = CreateSnapshot(hwnd => hwnd switch
        {
            IntPtr h when h == (IntPtr)70 => movedAway,
            IntPtr h when h == (IntPtr)71 => rectA,
            _ => default
        });

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)70, (IntPtr)71 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        // Fast path rejected (70 moved away) → rebuild → snapshot maps A→71 → accepted.
        Assert.Equal((IntPtr)71, hwnd);
        Assert.Equal((IntPtr)71, registry.GetHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft));
    }

    [Fact]
    public void ResolveHwndForSlot_ReturnsNull_WhenRegistryStaleAndLayoutNotRecognized()
    {
        // Registry maps TopLeft→80, but window 80 has been dragged far from slot A,
        // and the snapshot finds no window in slot A at all.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        registry.SetSlotsForMonitor(WorkArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)80
        });

        var rectA = SlotRect("A");
        var movedAway = new Rect(317, 244, rectA.Width - 500, rectA.Height - 500);
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)80 ? movedAway : default);

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)80 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        Assert.Null(hwnd);
    }

    [Fact]
    public void ResolveHwndForSlot_ReturnsRebuiltSlot_WhenSnapshotRecognizesLayout()
    {
        // Empty registry (e.g. after a desktop switch that purged nothing but the slot
        // was never re-registered). Window 90 is physically in slot B.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectB = SlotRect("B");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)90 ? rectB : default);

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)90 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.BottomRight);

        Assert.Equal((IntPtr)90, hwnd);
    }

    [Fact]
    public void ResolveHwndForSlot_RejectsRebuiltHwnd_WhenItNoLongerMatchesSlotRect()
    {
        // Registry is empty. Rebuild maps TopLeft→100, but by the time we re-validate
        // the rect (simulated via a rectFor that returns a non-matching rect for the
        // rebuild candidate), the mapping must be rejected. This is modelled by having
        // the snapshot source contain hwnd 100 but its rect not matching slot A.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectA = SlotRect("A");
        var movedAway = new Rect(317, 244, rectA.Width - 500, rectA.Height - 500);

        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)100 ? movedAway : default);

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)100 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        Assert.Null(hwnd);
        Assert.False(registry.HasSlotsForMonitor(WorkArea));
    }

    [Fact]
    public void ResolveHwndForSlot_DoesNotRebuild_WhenRegistrySlotStillValid()
    {
        // Registry maps TopLeft→110, window 110 still in slot A → no rebuild, no
        // candidate source call.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        registry.SetSlotsForMonitor(WorkArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)110
        });

        var rectA = SlotRect("A");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)110 ? rectA : default);

        var sourceCallCount = 0;
        var service = CreateService(
            registry,
            snapshot,
            () => { sourceCallCount++; return Array.Empty<IntPtr>(); });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopLeft);

        Assert.Equal((IntPtr)110, hwnd);
        Assert.Equal(0, sourceCallCount);
    }

    // --- Edge-centred slot E/F/G/H rebuild flow: a Ctrl+Shift+up/down/left/right gesture
    // must resolve via the same registry-miss → snapshot-rebuild path as the corner slots. ---

    [Fact]
    public void WhenRegistryMissesEdgeSlot_E_RebuildsAndResolves()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectE = SlotRect("E");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)120 ? rectE : default);

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)120 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopCenter);

        Assert.Equal((IntPtr)120, hwnd);
        Assert.Equal((IntPtr)120, registry.GetHwndForSlot(WorkArea, DirectionalFocusSlot.TopCenter));
    }

    [Fact]
    public void WhenRegistryMissesEdgeSlot_H_RebuildsAndResolves()
    {
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectH = SlotRect("H");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)121 ? rectH : default);

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)121 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.MiddleLeft);

        Assert.Equal((IntPtr)121, hwnd);
    }

    [Fact]
    public void WhenEdgeSlot_E_AbsentButAlignedSlotExists_FallsBackToIt()
    {
        // TopCenter (E) is empty, but slot A (TopLeft) is available and is aligned "up" with
        // the gesture. The fallback must return A instead of null — this is the fix for the
        // "6 windows, MiddleRight empty, gesture does nothing" UX bug.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectA = SlotRect("A");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)130 ? rectA : default);
        registry.SetSlotsForMonitor(WorkArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)130
        });

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)130 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopCenter);

        Assert.Equal((IntPtr)130, hwnd);
    }

    [Fact]
    public void WhenNoAlignedSlotExists_FallsBackToNull()
    {
        // Only slot A (TopLeft, up-left) is available, but the gesture targets BottomRight
        // (down-right) — the opposite direction. The fallback must NOT focus a window in the
        // opposite direction; it returns null cleanly.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var rectA = SlotRect("A");
        var snapshot = CreateSnapshot(hwnd => hwnd == (IntPtr)131 ? rectA : default);
        registry.SetSlotsForMonitor(WorkArea, new Dictionary<DirectionalFocusSlot, IntPtr>
        {
            [DirectionalFocusSlot.TopLeft] = (IntPtr)131
        });

        var service = CreateService(registry, snapshot, () => new[] { (IntPtr)131 });

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.BottomRight);

        Assert.Null(hwnd);
    }

    // --- 6-window scenario: corners A-D + centers E/F populated, edge slots G/H empty.
    // A gesture toward an empty edge slot must fall back to an aligned populated slot. ---

    private static (DirectionalFocusRegistry registry, DirectionalFocusLayoutSnapshotService snapshot, IReadOnlyList<IntPtr> hwnds)
        BuildSixWindowLayout()
    {
        // 6 windows: A(1)=TopLeft, B(2)=BottomRight, C(3)=TopRight, D(4)=BottomLeft,
        // E(5)=TopCenter, F(6)=BottomCenter. G(MiddleRight) and H(MiddleLeft) are EMPTY —
        // exactly the situation where the old code did nothing on a left/right gesture.
        var slots = new (string SlotId, DirectionalFocusSlot Slot, IntPtr Hwnd)[]
        {
            ("A", DirectionalFocusSlot.TopLeft, (IntPtr)1),
            ("B", DirectionalFocusSlot.BottomRight, (IntPtr)2),
            ("C", DirectionalFocusSlot.TopRight, (IntPtr)3),
            ("D", DirectionalFocusSlot.BottomLeft, (IntPtr)4),
            ("E", DirectionalFocusSlot.TopCenter, (IntPtr)5),
            ("F", DirectionalFocusSlot.BottomCenter, (IntPtr)6)
        };

        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var map = new Dictionary<DirectionalFocusSlot, IntPtr>();
        var rectByHwnd = new Dictionary<IntPtr, Rect>();
        foreach (var (slotId, slot, hwnd) in slots)
        {
            map[slot] = hwnd;
            rectByHwnd[hwnd] = SlotRect(slotId);
        }
        registry.SetSlotsForMonitor(WorkArea, map);

        var snapshot = CreateSnapshot(hwnd => rectByHwnd.TryGetValue(hwnd, out var r) ? r : default);
        var hwnds = slots.Select(s => s.Hwnd).ToList();
        return (registry, snapshot, hwnds);
    }

    [Fact]
    public void SixWindows_MiddleRightEmpty_FallsBackToTopRightOrBottomRight()
    {
        // Gesture "right" → MiddleRight (G), which is empty. Both TopRight (C) and
        // BottomRight (B) are aligned right; the fallback must return one of them.
        var (registry, snapshot, hwnds) = BuildSixWindowLayout();
        var service = CreateService(registry, snapshot, () => hwnds);

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.MiddleRight);

        Assert.NotNull(hwnd);
        // Must be a right-edge window (C=3 or B=2), never a left/center one.
        Assert.Contains(hwnd!.Value, new[] { (IntPtr)3, (IntPtr)2 });
    }

    [Fact]
    public void SixWindows_MiddleLeftEmpty_FallsBackToTopLeftOrBottomLeft()
    {
        // Gesture "left" → MiddleLeft (H), empty. Fallback must be a left-edge window.
        var (registry, snapshot, hwnds) = BuildSixWindowLayout();
        var service = CreateService(registry, snapshot, () => hwnds);

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.MiddleLeft);

        Assert.NotNull(hwnd);
        Assert.Contains(hwnd!.Value, new[] { (IntPtr)1, (IntPtr)4 });
    }

    [Fact]
    public void SixWindows_TopCenterPopulated_ReturnsItDirectly()
    {
        // Non-regression: when the exact slot IS populated, no fallback occurs.
        var (registry, snapshot, hwnds) = BuildSixWindowLayout();
        var service = CreateService(registry, snapshot, () => hwnds);

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.TopCenter);

        Assert.Equal((IntPtr)5, hwnd);
    }

    [Fact]
    public void SixWindows_BottomCenterPopulated_ReturnsItDirectly()
    {
        var (registry, snapshot, hwnds) = BuildSixWindowLayout();
        var service = CreateService(registry, snapshot, () => hwnds);

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.BottomCenter);

        Assert.Equal((IntPtr)6, hwnd);
    }

    [Fact]
    public void NoSlotsPopulated_ReturnsNull_LayoutNotRecognized()
    {
        // Zero slots on the monitor → genuinely "layout not recognized": return null so the
        // user-facing log can still suggest running Arrange Now.
        var registry = new DirectionalFocusRegistry(_ => true, _ => true);
        var snapshot = CreateSnapshot(_ => default);
        var service = CreateService(registry, snapshot, () => Array.Empty<IntPtr>());

        var hwnd = service.ResolveHwndForSlot(WorkArea, DirectionalFocusSlot.MiddleRight);

        Assert.Null(hwnd);
    }
}
