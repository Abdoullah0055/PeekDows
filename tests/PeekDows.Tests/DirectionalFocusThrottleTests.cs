using System;
using System.Collections.Generic;
using PeekDows.App.Focus;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

/// <summary>
/// Tests the global throttle and post-failure backoff of <see cref="DirectionalFocusService"/>
/// via its pure <c>IsThrottled</c>/<c>SetPostActivationCooldown</c> seams, without going
/// through the Win32 mouse path in <c>Tick</c>.
/// </summary>
public class DirectionalFocusThrottleTests
{
    private static readonly Rect WorkArea = new(0, 0, 1920, 1080);

    private sealed class FakeMonitorResolver : IMonitorResolver
    {
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd) => new() { WorkArea = WorkArea, IsPrimary = true };
        public MonitorInfo GetPrimaryMonitor() => new() { WorkArea = WorkArea, IsPrimary = true };
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

    private static DirectionalFocusService CreateService(Func<DateTime> clock)
    {
        return new DirectionalFocusService(
            new DirectionalFocusGestureDetector(),
            new DirectionalFocusRegistry(_ => true, _ => true),
            new FakeMonitorResolver(),
            new WindowActivationService(new AlwaysFailActivationApi(), clock),
            new AlwaysTrueVirtualDesktopService(),
            new DirectionalFocusLayoutSnapshotService(
                new FakeMonitorResolver(),
                new LayoutEngine(),
                _ => default,
                _ => true),
            () => Array.Empty<IntPtr>(),
            () => 80,
            new DirectionalFocusInputGate(),
            clock);
    }

    [Fact]
    public void FreshService_IsNotThrottled()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var service = CreateService(() => now);

        Assert.False(service.IsThrottled(now));
    }

    [Fact]
    public void AfterSuccessfulActivation_IsThrottledForCooldownWindow()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var service = CreateService(() => now);

        service.SetPostActivationCooldown(now, success: true);

        // 450ms cooldown: throttled just under, free just over.
        Assert.True(service.IsThrottled(now.AddMilliseconds(149)));
        Assert.False(service.IsThrottled(now.AddMilliseconds(151)));
    }

    [Fact]
    public void AfterFailedActivation_SameShortThrottle_NoLongBackoff()
    {
        // Fluidity fix: a foreground-mismatch used to trigger a 1450ms global backoff, which
        // made rapid slot-hopping feel sluggish. Now success and failure use the SAME short
        // pacing — the per-hwnd cooldown (and the unstable tracker for hung windows) carry
        // the heavy protection, so the global throttle only paces the gesture.
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var service = CreateService(() => now);

        service.SetPostActivationCooldown(now, success: false);

        Assert.True(service.IsThrottled(now.AddMilliseconds(149)));
        Assert.False(service.IsThrottled(now.AddMilliseconds(151)));
    }

    [Fact]
    public void RapidDirectionChanges_WhileHeld_AreThrottled()
    {
        // Models the gesture: user holds Ctrl+Shift and whips the mouse between slots. The
        // first activation applies the short cooldown; subsequent changes within that window
        // are throttled rather than each one firing an activation.
        var t = new DateTime(2026, 1, 1, 12, 0, 0);
        var service = CreateService(() => t);

        // First slot-hop fires and starts the cooldown.
        service.SetPostActivationCooldown(t, success: true);

        // Ticks later (each 40ms apart, like the real timer) — all throttled.
        Assert.True(service.IsThrottled(t.AddMilliseconds(40)));
        Assert.True(service.IsThrottled(t.AddMilliseconds(80)));
        Assert.True(service.IsThrottled(t.AddMilliseconds(120)));

        // Only after the short cooldown elapses can another activation proceed.
        Assert.False(service.IsThrottled(t.AddMilliseconds(151)));
    }

    [Fact]
    public void ThrottleExpiration_AllowsNextActivation()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var service = CreateService(() => now);

        service.SetPostActivationCooldown(now, success: true);

        var after = now.AddMilliseconds(500);
        Assert.False(service.IsThrottled(after));

        // A new activation can then re-arm the throttle.
        service.SetPostActivationCooldown(after, success: true);
        Assert.True(service.IsThrottled(after.AddMilliseconds(100)));
    }
}
