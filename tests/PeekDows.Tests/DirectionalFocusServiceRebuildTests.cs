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

        var snapshot = CreateSnapshot(_ => default);
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
}
