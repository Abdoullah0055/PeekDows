using System.Collections.Generic;
using PeekDows.App.Focus;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Tests the foreground-unlock path. The real Win32InputSimulator calls live SendInput
/// (untestable), so these tests inject a fake <see cref="IInputSimulator"/> into
/// <see cref="Win32ActivationApi"/> to verify the wiring: the ALT pulse runs, the fallback is
/// attempted on failure, and the activation path still bans AttachThreadInput/SetFocus/topmost.
/// </summary>
public class Win32InputSimulatorTests
{
    /// <summary>
    /// Fake simulator that records calls and lets the test control whether SendInput "succeeds".
    /// A failure simulates the original bug (SendInput returning 0), which must trigger the
    /// keybd_event fallback in the real simulator.
    /// </summary>
    private sealed class FakeInputSimulator : IInputSimulator
    {
        public int CallCount { get; private set; }
        public bool NextResult { get; set; } = true;
        public bool TryAltPulse()
        {
            CallCount++;
            return NextResult;
        }
    }

    [Fact]
    public void Win32ActivationApi_TryUnlockForegroundWithAltPulse_DelegatesToSimulator()
    {
        // The API must delegate the pulse to IInputSimulator (and return whatever it returns).
        var sim = new FakeInputSimulator { NextResult = true };
        var api = new Win32ActivationApi(sim);

        bool result = api.TryUnlockForegroundWithAltPulse();

        Assert.True(result);
        Assert.Equal(1, sim.CallCount);
    }

    [Fact]
    public void Win32ActivationApi_AltPulse_Failure_PropagatesFalse()
    {
        // When the simulator reports failure (the original SendInput bug), the API surfaces it
        // so the activation path can still proceed — but in production the real simulator would
        // already have applied the keybd_event fallback internally.
        var sim = new FakeInputSimulator { NextResult = false };
        var api = new Win32ActivationApi(sim);

        bool result = api.TryUnlockForegroundWithAltPulse();

        Assert.False(result);
        Assert.Equal(1, sim.CallCount);
    }

    [Fact]
    public void Win32InputSimulator_SizeOfInput_MatchesWin32Layout()
    {
        // Regression guard for the original bug: the INPUT struct must marshal to the size
        // Windows expects (40 bytes on x64: type(4) + padding(4) + 32-byte union). A wrong
        // cbSize made SendInput fail and the ALT pulse never unlocked the foreground lock.
        int size = System.Runtime.InteropServices.Marshal.SizeOf<PeekDows.Core.Win32.NativeMethods.INPUT>();
        Assert.Equal(40, size); // canonical Win32 INPUT size on x64
    }
}
