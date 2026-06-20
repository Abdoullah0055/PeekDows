using System;
using System.Text;
using PeekDows.App.Focus;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.Tests;

public class FakeActivationApi : IWindowActivationApi
{
    public bool IsWindowResult { get; set; } = true;
    public bool IsIconicResult { get; set; } = false;
    public bool ShowWindowResult { get; set; } = true;
    public IntPtr ForegroundWindowResult { get; set; } = IntPtr.Zero;
    public uint ForegroundThreadId { get; set; } = 100;
    public uint TargetThreadId { get; set; } = 200;
    public uint CurrentThreadIdResult { get; set; } = 50;
    public bool AttachThreadInputResult { get; set; } = true;
    public bool BringWindowToTopResult { get; set; } = true;
    public bool SetWindowPosResult { get; set; } = true;
    public bool SetForegroundWindowResult { get; set; } = true;
    public IntPtr SetFocusResult { get; set; } = IntPtr.Zero;

    /// <summary>Default: windows respond. Set to false to simulate a hung/unresponsive window.</summary>
    public bool IsResponsiveResult { get; set; } = true;

    /// <summary>Result returned by the ALT-pulse helper; defaults to delivered.</summary>
    public bool AltPulseResult { get; set; } = true;

    public int IsWindowCallCount { get; private set; }
    public int ShowWindowCallCount { get; private set; }
    public int ShowWindowAsyncCallCount { get; private set; }
    public int BringWindowToTopCallCount { get; private set; }
    public int SetWindowPosCallCount { get; private set; }
    public int SetForegroundWindowCallCount { get; private set; }
    public int SetFocusCallCount { get; private set; }
    public int AttachThreadInputCallCount { get; private set; }
    public int DetachThreadInputCallCount { get; private set; }
    public int IsResponsiveCallCount { get; private set; }
    public int AltPulseCallCount { get; private set; }

    private IntPtr _targetHwnd;
    private bool _setForegroundCalled;
    private IntPtr _foregroundAfterActivation;

    public void SetForegroundWindowUpdatesForeground(IntPtr newForeground)
    {
        _foregroundAfterActivation = newForeground;
    }

    public bool IsWindow(IntPtr hwnd) { IsWindowCallCount++; _targetHwnd = hwnd; return IsWindowResult; }
    public bool IsIconic(IntPtr hwnd) => IsIconicResult;
    public bool ShowWindow(IntPtr hwnd, int cmdShow) { ShowWindowCallCount++; return ShowWindowResult; }
    public bool ShowWindowAsync(IntPtr hwnd, int cmdShow) { ShowWindowAsyncCallCount++; return ShowWindowResult; }
    public IntPtr GetForegroundWindow()
    {
        if (_setForegroundCalled && _foregroundAfterActivation != IntPtr.Zero)
            return _foregroundAfterActivation;
        return ForegroundWindowResult;
    }
    public uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid) { pid = 0; return hwnd == _targetHwnd ? TargetThreadId : ForegroundThreadId; }
    public uint GetCurrentThreadId() => CurrentThreadIdResult;
    public bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach)
    {
        if (fAttach) AttachThreadInputCallCount++; else DetachThreadInputCallCount++;
        return AttachThreadInputResult;
    }
    public bool BringWindowToTop(IntPtr hwnd) { BringWindowToTopCallCount++; return BringWindowToTopResult; }
    public bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags)
    {
        SetWindowPosCallCount++;
        if (hwndInsertAfter == NativeMethods.HWND_TOPMOST) SetWindowPosUsedTopmost = true;
        if (hwndInsertAfter == NativeMethods.HWND_NOTOPMOST) SetWindowPosUsedNoTopmost = true;
        return SetWindowPosResult;
    }
    public bool SetWindowPosUsedTopmost { get; private set; }
    public bool SetWindowPosUsedNoTopmost { get; private set; }
    public bool SetForegroundWindow(IntPtr hwnd) { SetForegroundWindowCallCount++; _setForegroundCalled = true; return SetForegroundWindowResult; }
    public IntPtr SetFocus(IntPtr hwnd) { SetFocusCallCount++; return SetFocusResult; }
    public int GetWindowText(IntPtr hwnd, StringBuilder sb, int maxCount) { sb.Append("TestWindow"); return 10; }
    public uint GetWindowProcessId(IntPtr hwnd) => 1234;
    public bool IsResponsive(IntPtr hwnd, uint timeoutMs) { IsResponsiveCallCount++; return IsResponsiveResult; }
    public bool TryUnlockForegroundWithAltPulse() { AltPulseCallCount++; return AltPulseResult; }
}

public class WindowActivationServiceTests
{
    [Fact]
    public void Activate_InvalidHandle_ReturnsForegroundDenied()
    {
        var api = new FakeActivationApi();
        var service = new WindowActivationService(api);
        Assert.Equal(ActivationStatus.ForegroundDenied, service.Activate(IntPtr.Zero));
        Assert.Equal(0, api.IsWindowCallCount);
    }

    [Fact]
    public void Activate_IsWindowFalse_ReturnsForegroundDenied()
    {
        var api = new FakeActivationApi { IsWindowResult = false };
        var service = new WindowActivationService(api);
        Assert.Equal(ActivationStatus.ForegroundDenied, service.Activate((IntPtr)100));
        Assert.Equal(1, api.IsWindowCallCount);
    }

    [Fact]
    public void Activate_RestoresMinimizedWindow()
    {
        var api = new FakeActivationApi { IsIconicResult = true };
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        // Restore now uses the NON-BLOCKING ShowWindowAsync (hardening: a hung target must
        // not block activation). The blocking ShowWindow must NOT be used on the normal path.
        Assert.Equal(1, api.ShowWindowAsyncCallCount);
        Assert.Equal(0, api.ShowWindowCallCount);
    }

    [Fact]
    public void Activate_SkipsRestore_WhenNotMinimized()
    {
        var api = new FakeActivationApi { IsIconicResult = false };
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.Equal(0, api.ShowWindowCallCount);
    }

    [Fact]
    public void Activate_DoesNotAttachThreadInput_OnNormalPath()
    {
        // Hardening: the normal activation path must never call AttachThreadInput. Attaching
        // synchronises PeekDows's UI thread queue with the target's, so a hung target freezes
        // PeekDows. This replaces the old AttachThreadInput-OnDifferentThreads behaviour.
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.Equal(0, api.AttachThreadInputCallCount);
        Assert.Equal(0, api.DetachThreadInputCallCount);
    }

    [Fact]
    public void Activate_DoesNotCallBringWindowToTop_OnNormalPath()
    {
        // Hardening: BringWindowToTop was removed from the normal path. The single
        // SetWindowPos(HWND_TOP, SWP_ASYNCWINDOWPOS) that follows supersedes it, and
        // BringWindowToTop re-issues against the target thread (a potential freeze vector).
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.Equal(0, api.BringWindowToTopCallCount);
    }

    [Fact]
    public void Activate_HwndAlreadyUnstable_SkipsAllWin32Calls()
    {
        // If a window was marked unstable by WindowPlacementService (e.g. a slow SetWindowPos
        // during the last arrange), Directional Focus must NOT activate it: no probe, no
        // restore, no SetWindowPos, no SetForegroundWindow. This is the cross-path circuit
        // breaker between arrange and activation.
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var tracker = new UnstableWindowTracker(() => now, null);
        tracker.MarkUnstable((IntPtr)100, now: now, reason: "slow-setwindowpos-700ms");

        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api, () => now, null, tracker);

        var status = service.Activate((IntPtr)100);

        Assert.Equal(ActivationStatus.UnstableSkipped, status);
        Assert.Equal(0, api.IsResponsiveCallCount);
        Assert.Equal(0, api.ShowWindowAsyncCallCount);
        Assert.Equal(0, api.SetWindowPosCallCount);
        Assert.Equal(0, api.SetForegroundWindowCallCount);
        Assert.Equal(0, api.BringWindowToTopCallCount);
    }

    [Fact]
    public void Activate_CallsSetWindowPosTop()
    {
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.True(api.SetWindowPosCallCount >= 1);
    }

    // --- Hardening: normal path must not call SetFocus, must not run a topmost fallback,
    // and must not issue a second SetForegroundWindow. These are the freeze vectors that
    // were removed per the spec. ---

    [Fact]
    public void Activate_DoesNotCallSetFocus_OnNormalPath()
    {
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.Equal(0, api.SetFocusCallCount);
    }

    [Fact]
    public void Activate_OnFailure_DoesNotCallTopmostFallback()
    {
        // A failed activation must NOT escalate into SetWindowPos(HWND_TOPMOST) or
        // SetWindowPos(HWND_NOTOPMOST). The fake records hwndInsertAfter for every
        // SetWindowPos call; both topmost sentinels must be absent.
        var api = new FakeActivationApi { SetForegroundWindowResult = false };
        api.ForegroundWindowResult = (IntPtr)999;
        var service = new WindowActivationService(api);

        service.Activate((IntPtr)100);

        Assert.False(api.SetWindowPosUsedTopmost, "SetWindowPos(HWND_TOPMOST) must not be called on failure");
        Assert.False(api.SetWindowPosUsedNoTopmost, "SetWindowPos(HWND_NOTOPMOST) must not be called on failure");
    }

    [Fact]
    public void Activate_OnFailure_DoesNotIssueSecondSetForegroundWindow()
    {
        // The old topmost fallback called SetForegroundWindow a second time. The hardening
        // path issues exactly one, then backs off into cooldown.
        var api = new FakeActivationApi { SetForegroundWindowResult = false };
        api.ForegroundWindowResult = (IntPtr)999;
        var service = new WindowActivationService(api);

        service.Activate((IntPtr)100);

        Assert.Equal(1, api.SetForegroundWindowCallCount);
    }

    [Fact]
    public void Activate_OnFailure_StartsHwndCooldownImmediately()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var api = new FakeActivationApi { SetForegroundWindowResult = false };
        api.ForegroundWindowResult = (IntPtr)999;
        var service = new WindowActivationService(api, () => now);

        service.Activate((IntPtr)100);

        Assert.True(service.IsInCooldown((IntPtr)100, now));
    }

    [Fact]
    public void Activate_CallsSetForegroundWindow()
    {
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.True(api.SetForegroundWindowCallCount >= 1);
    }

    [Fact]
    public void Activate_OnFailure_DoesNotAttachOrDetachThreadInput()
    {
        // Hardening: even on activation failure, the normal path must not have attached (so
        // nothing to detach) and must not run the old topmost fallback that called extra
        // SetWindowPos/SetForegroundWindow. This replaces the old DetachesThreadInput-InFinally
        // test, which documented the now-removed attach/detach pairing.
        var api = new FakeActivationApi { SetForegroundWindowResult = false };
        api.ForegroundWindowResult = (IntPtr)999;
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.Equal(0, api.AttachThreadInputCallCount);
        Assert.Equal(0, api.DetachThreadInputCallCount);
    }

    [Fact]
    public void Activate_ReturnsPending_WhenForegroundMismatch()
    {
        // Foreground not yet confirmed synchronously → Pending (NOT a hard failure). A delayed
        // recheck is scheduled; the caller must not treat this as failure.
        var api = new FakeActivationApi();
        api.ForegroundWindowResult = (IntPtr)999;
        var service = new WindowActivationService(api);
        Assert.Equal(ActivationStatus.Pending, service.Activate((IntPtr)100));
    }

    [Fact]
    public void Activate_ReturnsTrue_WhenTargetIsForeground()
    {
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        Assert.Equal(ActivationStatus.Success, service.Activate((IntPtr)100));
    }

    // --- Hardening: unresponsive windows must never block PeekDows. ---

    [Fact]
    public void Activate_UnresponsiveWindow_SkipsActivation_WithoutBlockingCalls()
    {
        // Simulate a hung window: the responsiveness probe returns false.
        var api = new FakeActivationApi { IsResponsiveResult = false };
        var service = new WindowActivationService(api);

        var status = service.Activate((IntPtr)100);

        Assert.Equal(ActivationStatus.NotResponding, status);
        // The probe ran, but none of the blocking activation calls were attempted.
        Assert.Equal(1, api.IsResponsiveCallCount);
        Assert.Equal(0, api.AttachThreadInputCallCount);
        Assert.Equal(0, api.SetForegroundWindowCallCount);
        Assert.Equal(0, api.SetFocusCallCount);
        Assert.Equal(0, api.BringWindowToTopCallCount);
    }

    [Fact]
    public void Activate_ResponsiveWindow_ProceedsWithActivation()
    {
        // Sanity: the hung-check must not change the happy path for a responsive window.
        var api = new FakeActivationApi { IsResponsiveResult = true };
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);

        var status = service.Activate((IntPtr)100);

        Assert.Equal(ActivationStatus.Success, status);
        Assert.Equal(1, api.IsResponsiveCallCount);
        Assert.True(api.SetForegroundWindowCallCount >= 1);
    }

    // --- Foreground unlock: the ALT pulse is the safe alternative to AttachThreadInput.
    // It must be invoked on the normal path, before SetForegroundWindow. ---

    [Fact]
    public void Activate_NormalPath_CallsAltPulseBeforeSetForegroundWindow()
    {
        var api = new FakeActivationApi { IsResponsiveResult = true };
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);

        service.Activate((IntPtr)100);

        // The ALT pulse ran exactly once and the foreground request followed it.
        Assert.Equal(1, api.AltPulseCallCount);
        Assert.True(api.SetForegroundWindowCallCount >= 1);
    }

    [Fact]
    public void Activate_NormalPath_NeverCallsAttachThreadInput_SetFocus_Topmost()
    {
        // Regression guard for the hardening guarantees: the dangerous calls stay banned from
        // the normal path, foreground unlock is done via ALT pulse instead.
        var api = new FakeActivationApi { IsResponsiveResult = true };
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);

        service.Activate((IntPtr)100);

        Assert.Equal(0, api.AttachThreadInputCallCount);
        Assert.Equal(0, api.SetFocusCallCount);
        Assert.False(api.SetWindowPosUsedTopmost);
        Assert.False(api.SetWindowPosUsedNoTopmost);
    }

    [Fact]
    public void Activate_PendingResult_DoesNotMarkUnstableTracker()
    {
        // Foreground mismatch returns Pending, schedules a delayed recheck, and must NOT mark
        // the window unstable (15s). Healthy windows stay arrangeable and focusable.
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var tracker = new UnstableWindowTracker(() => now, null);
        var api = new FakeActivationApi { IsResponsiveResult = true };
        api.ForegroundWindowResult = (IntPtr)999; // mismatch → Pending
        var service = new WindowActivationService(api, () => now, null, tracker);

        var status = service.Activate((IntPtr)100);

        Assert.Equal(ActivationStatus.Pending, status);
        Assert.False(tracker.IsUnstable((IntPtr)100));
    }

    [Fact]
    public void Activate_ForegroundMismatch_AppliesShortCooldown_AndDoesNotMarkUnstable()
    {
        // A foreground-mismatch is NOT a dangerous Win32 signal (Windows routinely denies
        // SetForegroundWindow). It must apply only a SHORT local cooldown and must NOT reach
        // the UnstableWindowTracker — otherwise healthy windows get excluded for 15s, which
        // is what made the shortcut sluggish.
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var tracker = new UnstableWindowTracker(() => now, null);
        var api = new FakeActivationApi();
        api.ForegroundWindowResult = (IntPtr)999; // foreground never matches → mismatch
        var service = new WindowActivationService(api, () => now, null, tracker);

        service.Activate((IntPtr)100);

        // Short cooldown (~400ms) so rapid retries are paced...
        Assert.True(service.IsInCooldown((IntPtr)100, now));
        Assert.True(service.IsInCooldown((IntPtr)100, now.AddMilliseconds(399)));
        Assert.False(service.IsInCooldown((IntPtr)100, now.AddMilliseconds(401)));
        // ...but the window is NOT marked unstable, so arrange and the A-H registry keep it.
        Assert.False(tracker.IsUnstable((IntPtr)100));
    }

    [Fact]
    public void Activate_NotResponding_MarksUnstableFor15Seconds()
    {
        // A truly unresponsive window IS a dangerous signal → tracker (15s), regardless of
        // the shorter foreground-mismatch cooldown. This guards the anti-freeze protection
        // is retained for real hung windows.
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var tracker = new UnstableWindowTracker(() => now, null);
        var api = new FakeActivationApi { IsResponsiveResult = false };
        var service = new WindowActivationService(api, () => now, null, tracker);

        service.Activate((IntPtr)100);

        Assert.True(tracker.IsUnstable((IntPtr)100, now));
        Assert.True(tracker.IsUnstable((IntPtr)100, now.AddMilliseconds(14999)));
        Assert.False(tracker.IsUnstable((IntPtr)100, now.AddMilliseconds(15001)));
    }

    [Fact]
    public void Activate_HwndInCooldown_IsSkippedWithoutProbe()
    {
        // Once on cooldown, the window is skipped entirely on subsequent calls — not even
        // the responsiveness probe should fire, so a still-hung app costs zero Win32 calls
        // on every later tick until the cooldown elapses.
        var clock = new DateTime(2026, 1, 1, 12, 0, 0);
        var api = new FakeActivationApi { IsResponsiveResult = false };
        var service = new WindowActivationService(api, () => clock);

        service.Activate((IntPtr)100); // hung → cooldown starts, probe called once
        int probesAfterFirst = api.IsResponsiveCallCount;
        Assert.Equal(1, probesAfterFirst);

        clock = clock.AddMilliseconds(500); // still within the 3000ms cooldown
        service.Activate((IntPtr)100);
        service.Activate((IntPtr)100);

        // Probe must not have run again while the cooldown is active.
        Assert.Equal(probesAfterFirst, api.IsResponsiveCallCount);
        Assert.Equal(0, api.SetForegroundWindowCallCount);
    }

    [Fact]
    public void Activate_CooldownExpires_AllowsRetry()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var api = new FakeActivationApi { IsResponsiveResult = false };
        var service = new WindowActivationService(api, () => now);

        service.Activate((IntPtr)100); // hung → cooldown for 3000ms
        Assert.False(service.IsInCooldown((IntPtr)100, now.AddMilliseconds(3500)));

        // After cooldown, activation is attempted again (probe fires).
        var after = now.AddMilliseconds(3500);
        var api2 = new FakeActivationApi { IsResponsiveResult = true };
        api2.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service2 = new WindowActivationService(api2, () => after);

        var status = service2.Activate((IntPtr)100);

        Assert.Equal(ActivationStatus.Success, status);
        Assert.Equal(1, api2.IsResponsiveCallCount);
    }

    [Fact]
    public void Activate_ZeroHandle_DoesNotProbe()
    {
        var api = new FakeActivationApi();
        var service = new WindowActivationService(api);

        service.Activate(IntPtr.Zero);

        Assert.Equal(0, api.IsResponsiveCallCount);
    }

    [Fact]
    public void Activate_InvalidHandle_DoesNotProbe()
    {
        var api = new FakeActivationApi { IsWindowResult = false };
        var service = new WindowActivationService(api);

        service.Activate((IntPtr)100);

        Assert.Equal(0, api.IsResponsiveCallCount);
    }
}
