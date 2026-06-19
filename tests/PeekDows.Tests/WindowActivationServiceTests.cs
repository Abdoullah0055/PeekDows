using System;
using System.Text;
using PeekDows.App.Focus;
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

    public int IsWindowCallCount { get; private set; }
    public int ShowWindowCallCount { get; private set; }
    public int BringWindowToTopCallCount { get; private set; }
    public int SetWindowPosCallCount { get; private set; }
    public int SetForegroundWindowCallCount { get; private set; }
    public int SetFocusCallCount { get; private set; }
    public int AttachThreadInputCallCount { get; private set; }
    public int DetachThreadInputCallCount { get; private set; }
    public int IsResponsiveCallCount { get; private set; }

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
    public bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags) { SetWindowPosCallCount++; return SetWindowPosResult; }
    public bool SetForegroundWindow(IntPtr hwnd) { SetForegroundWindowCallCount++; _setForegroundCalled = true; return SetForegroundWindowResult; }
    public IntPtr SetFocus(IntPtr hwnd) { SetFocusCallCount++; return SetFocusResult; }
    public int GetWindowText(IntPtr hwnd, StringBuilder sb, int maxCount) { sb.Append("TestWindow"); return 10; }
    public uint GetWindowProcessId(IntPtr hwnd) => 1234;
    public bool IsResponsive(IntPtr hwnd, uint timeoutMs) { IsResponsiveCallCount++; return IsResponsiveResult; }
}

public class WindowActivationServiceTests
{
    [Fact]
    public void Activate_InvalidHandle_ReturnsFalse()
    {
        var api = new FakeActivationApi();
        var service = new WindowActivationService(api);
        Assert.False(service.Activate(IntPtr.Zero));
        Assert.Equal(0, api.IsWindowCallCount);
    }

    [Fact]
    public void Activate_IsWindowFalse_ReturnsFalse()
    {
        var api = new FakeActivationApi { IsWindowResult = false };
        var service = new WindowActivationService(api);
        Assert.False(service.Activate((IntPtr)100));
        Assert.Equal(1, api.IsWindowCallCount);
    }

    [Fact]
    public void Activate_RestoresMinimizedWindow()
    {
        var api = new FakeActivationApi { IsIconicResult = true };
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.Equal(1, api.ShowWindowCallCount);
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
    public void Activate_AttachesThreadInput_WhenDifferentThreads()
    {
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.True(api.AttachThreadInputCallCount >= 1);
    }

    [Fact]
    public void Activate_CallsBringWindowToTop()
    {
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.Equal(1, api.BringWindowToTopCallCount);
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
    public void Activate_DetachesThreadInput_InFinally()
    {
        var api = new FakeActivationApi { SetForegroundWindowResult = false };
        api.ForegroundWindowResult = (IntPtr)999;
        var service = new WindowActivationService(api);
        service.Activate((IntPtr)100);
        Assert.True(api.DetachThreadInputCallCount >= api.AttachThreadInputCallCount);
    }

    [Fact]
    public void Activate_ReturnsFalse_WhenForegroundMismatch()
    {
        var api = new FakeActivationApi();
        api.ForegroundWindowResult = (IntPtr)999;
        var service = new WindowActivationService(api);
        bool result = service.Activate((IntPtr)100);
        Assert.False(result);
    }

    [Fact]
    public void Activate_ReturnsTrue_WhenTargetIsForeground()
    {
        var api = new FakeActivationApi();
        api.SetForegroundWindowUpdatesForeground((IntPtr)100);
        var service = new WindowActivationService(api);
        bool result = service.Activate((IntPtr)100);
        Assert.True(result);
    }

    // --- Hardening: unresponsive windows must never block PeekDows. ---

    [Fact]
    public void Activate_UnresponsiveWindow_SkipsActivation_WithoutBlockingCalls()
    {
        // Simulate a hung window: the responsiveness probe returns false.
        var api = new FakeActivationApi { IsResponsiveResult = false };
        var service = new WindowActivationService(api);

        bool result = service.Activate((IntPtr)100);

        Assert.False(result);
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

        bool result = service.Activate((IntPtr)100);

        Assert.True(result);
        Assert.Equal(1, api.IsResponsiveCallCount);
        Assert.True(api.SetForegroundWindowCallCount >= 1);
    }

    [Fact]
    public void Activate_FailedActivation_PutsHwndInCooldown()
    {
        // A failed activation (foreground mismatch) must place the hwnd on cooldown so it
        // isn't retried immediately. Uses a controllable clock.
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var api = new FakeActivationApi();
        api.ForegroundWindowResult = (IntPtr)999; // foreground never matches → failure
        var service = new WindowActivationService(api, () => now);

        service.Activate((IntPtr)100);

        Assert.True(service.IsInCooldown((IntPtr)100, now));
        Assert.True(service.IsInCooldown((IntPtr)100, now.AddMilliseconds(2999)));
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

        bool result = service2.Activate((IntPtr)100);

        Assert.True(result);
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
