using System;
using System.Collections.Generic;
using PeekDows.App.Animation;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class WindowAnimationServiceTests
{
    private static readonly Rect StartRect = new(0, 0, 800, 600);
    private static readonly Rect TargetRect = new(128, 72, 1152, 648);

    private sealed class FakeAnimationApi : IWindowAnimationApi
    {
        public Dictionary<IntPtr, Rect> Rects = new();
        public Dictionary<IntPtr, Rect> WorkAreas = new();
        public HashSet<IntPtr> InvalidWindows = new();
        public HashSet<IntPtr> MaximizedWindows = new();
        public List<(IntPtr Hwnd, Rect Rect, bool BringToFront)> SetWindowPosCalls = new();
        public List<IntPtr> MaximizeCalls = new();
        public List<IntPtr> RestoreCalls = new();
        public bool FailSetWindowPos;
        public bool MouseButtonsPressed;

        public bool IsWindow(IntPtr hwnd) => !InvalidWindows.Contains(hwnd);
        public bool IsZoomed(IntPtr hwnd) => MaximizedWindows.Contains(hwnd);

        public bool SetWindowPos(IntPtr hwnd, Rect rect, bool bringToFront = false)
        {
            if (FailSetWindowPos) return false;
            SetWindowPosCalls.Add((hwnd, rect, bringToFront));
            Rects[hwnd] = rect;
            return true;
        }

        public bool Maximize(IntPtr hwnd)
        {
            MaximizeCalls.Add(hwnd);
            MaximizedWindows.Add(hwnd);
            return true;
        }

        public bool RestoreWindow(IntPtr hwnd) { RestoreCalls.Add(hwnd); return true; }
        public Rect GetWorkAreaForWindow(IntPtr hwnd) => WorkAreas.TryGetValue(hwnd, out var r) ? r : new Rect(0, 0, 1920, 1080);

        public bool TryGetRect(IntPtr hwnd, out Rect rect) => Rects.TryGetValue(hwnd, out rect!);

        public bool AreMouseButtonsPressed() => MouseButtonsPressed;
        public System.Drawing.Point GetCursorPosition() => CursorPos;
        public System.Drawing.Point CursorPos = new(0, 0);
        public IntPtr WindowUnderCursor = IntPtr.Zero;
        public IntPtr WindowFromPoint(System.Drawing.Point pt) => WindowUnderCursor;
    }

    private sealed class FakeClock
    {
        public DateTime Now { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        public DateTime Get() => Now;
        public void AdvanceMs(int ms) => Now = Now.AddMilliseconds(ms);
    }

    private static IntPtr H(int id) => (IntPtr)id;

    private static (WindowAnimationService svc, FakeAnimationApi api, FakeClock clock) Create()
    {
        var api = new FakeAnimationApi();
        var clock = new FakeClock();
        var svc = new WindowAnimationService(api, clock.Get);
        api.Rects[H(1)] = StartRect;
        return (svc, api, clock);
    }

    [Fact]
    public void Begin_ThenTicks_ProgressTowardTargetWithEasing()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);
        Assert.True(svc.IsAnimating(H(1)));

        clock.AdvanceMs(75); // halfway through the 150ms duration
        svc.Tick();

        var call = Assert.Single(api.SetWindowPosCalls);
        // Ease-out cubic at t=0.5 gives 0.875 progress; exact same math on both sides.
        var expected = WindowAnimationService.LerpRect(StartRect, TargetRect, WindowAnimationService.EaseOutCubic(0.5));
        Assert.Equal(expected, call.Rect);
        Assert.True(svc.IsAnimating(H(1)));
    }

    [Fact]
    public void FinalTick_SnapsExactTarget_AndStopsAnimating()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);

        clock.AdvanceMs(150);
        svc.Tick();

        Assert.Equal(TargetRect, api.Rects[H(1)]);
        Assert.False(svc.IsAnimating(H(1)));
    }

    [Fact]
    public void MaximizeKind_FinalFrameIssuesNativeMaximize()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), new Rect(0, 0, 1920, 1080), PlacementKind.Maximize);

        clock.AdvanceMs(150);
        svc.Tick();

        Assert.Contains(H(1), api.MaximizeCalls);
        Assert.False(svc.IsAnimating(H(1)));
    }

    [Fact]
    public void MaximizeKind_AlreadyZoomed_IsNoOpButSucceeds()
    {
        var (svc, api, clock) = Create();
        api.MaximizedWindows.Add(H(1));

        bool began = svc.Begin(H(1), new Rect(0, 0, 1920, 1080), PlacementKind.Maximize);

        Assert.True(began);
        Assert.False(svc.IsAnimating(H(1)));
        Assert.Empty(api.MaximizeCalls);
    }

    [Fact]
    public void NewerBegin_SupersedesOlderTween()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);

        clock.AdvanceMs(40);
        var newTarget = new Rect(0, 0, 640, 480);
        svc.Begin(H(1), newTarget, PlacementKind.Reposition);

        clock.AdvanceMs(150);
        svc.Tick();

        // The final snap must go to the NEW target, not the first one.
        Assert.Equal(newTarget, api.Rects[H(1)]);
        Assert.False(svc.IsAnimating(H(1)));
    }

    [Fact]
    public void UserGrab_DeviationBeyondThreshold_CancelsTween()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);

        // Advance past the grab-detection grace period, then simulate the user dragging
        // the window far away from where the tween should be — with a mouse button held.
        clock.AdvanceMs(75);
        api.MouseButtonsPressed = true;
        api.WindowUnderCursor = H(1);
        api.Rects[H(1)] = new Rect(600, 400, 800, 600);
        svc.Tick();

        Assert.False(svc.IsAnimating(H(1)));
        // The animator must NOT have written its expected rect over the user's position.
        Assert.Equal(new Rect(600, 400, 800, 600), api.Rects[H(1)]);
    }

    [Fact]
    public void DeviationWithoutMouseButtons_DoesNotCancel_AsyncLagIsNotAGrab()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);

        // Past grace, the window lags far behind the commanded path (async SetWindowPos
        // still in flight) but NO mouse button is held — this must NOT be a grab:
        // production logs show false "user grab" cancels exactly in this situation.
        clock.AdvanceMs(75);
        api.Rects[H(1)] = new Rect(300, 200, 1000, 660);
        svc.Tick();

        Assert.True(svc.IsAnimating(H(1)));
        // The animator kept driving the tween toward the target.
        Assert.NotEmpty(api.SetWindowPosCalls);
    }

    [Fact]
    public void DeviationDuringGracePeriod_DoesNotCancel()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.RestoreAndReposition);

        // Within the 50ms grace: async restore landing is not a user grab.
        clock.AdvanceMs(16);
        api.Rects[H(1)] = new Rect(300, 200, 900, 700);
        svc.Tick();

        Assert.True(svc.IsAnimating(H(1)));
    }

    [Fact]
    public void VanishedWindow_TweenDropped()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);

        clock.AdvanceMs(16);
        api.InvalidWindows.Add(H(1));
        svc.Tick();

        Assert.False(svc.IsAnimating(H(1)));
    }

    [Fact]
    public void SetWindowPosFailure_CancelsTween()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);

        clock.AdvanceMs(16);
        api.FailSetWindowPos = true;
        svc.Tick();

        Assert.False(svc.IsAnimating(H(1)));
    }

    [Fact]
    public void SnapAllToTarget_WritesFinalRects_IncludingMaximize_AndClears()
    {
        var (svc, api, clock) = Create();
        svc.Begin(H(1), TargetRect, PlacementKind.Reposition);
        api.Rects[H(2)] = new Rect(10, 10, 400, 300);
        svc.Begin(H(2), new Rect(0, 0, 1920, 1080), PlacementKind.Maximize);

        clock.AdvanceMs(30);
        svc.SnapAllToTarget();

        Assert.Equal(TargetRect, api.Rects[H(1)]);
        Assert.Contains(H(2), api.MaximizeCalls);
        Assert.False(svc.IsAnimating(H(1)));
        Assert.False(svc.IsAnimating(H(2)));

        // Subsequent ticks are no-ops (registry empty) and must not write again.
        int callCount = api.SetWindowPosCalls.Count;
        svc.Tick();
        Assert.Equal(callCount, api.SetWindowPosCalls.Count);
    }

    [Fact]
    public void Begin_InvalidWindow_ReturnsFalse()
    {
        var (svc, api, _) = Create();
        api.InvalidWindows.Add(H(1));
        Assert.False(svc.Begin(H(1), TargetRect, PlacementKind.Reposition));
    }

    [Fact]
    public void Begin_AlreadyAtTarget_ReturnsTrueWithoutAnimating()
    {
        var (svc, api, _) = Create();
        api.Rects[H(1)] = TargetRect;
        bool began = svc.Begin(H(1), TargetRect, PlacementKind.Reposition);
        Assert.True(began);
        Assert.False(svc.IsAnimating(H(1)));
    }
}
