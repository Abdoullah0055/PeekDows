using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class DirectionalFocusInputGateTests
{
    private readonly DirectionalFocusInputGate _gate = new();

    [Fact]
    public void CtrlOnly_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: false, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void WinOnly_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: false, shiftDown: false, altDown: false, lWinDown: true, rWinDown: false));
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: false, shiftDown: false, altDown: false, lWinDown: false, rWinDown: true));
    }

    [Fact]
    public void CtrlLWin_ReturnsTrue()
    {
        Assert.True(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: false, lWinDown: true, rWinDown: false));
    }

    [Fact]
    public void CtrlRWin_ReturnsTrue()
    {
        Assert.True(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: false, lWinDown: false, rWinDown: true));
    }

    [Fact]
    public void Nothing_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: false, shiftDown: false, altDown: false, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void CtrlShift_ReturnsFalse()
    {
        // The retired chord: must never arm (OS layout toggle).
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: false, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void CtrlWinShift_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: false, lWinDown: true, rWinDown: false));
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: false, lWinDown: false, rWinDown: true));
    }

    [Fact]
    public void CtrlWinAlt_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: true, lWinDown: true, rWinDown: false));
    }

    [Fact]
    public void CtrlAlt_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: true, lWinDown: false, rWinDown: false));
    }
}
