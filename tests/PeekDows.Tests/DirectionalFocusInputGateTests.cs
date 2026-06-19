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
    public void ShiftOnly_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: false, shiftDown: true, altDown: false, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void CtrlShift_ReturnsTrue()
    {
        Assert.True(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: false, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void Nothing_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: false, shiftDown: false, altDown: false, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void CtrlAlt_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: true, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void CtrlShiftAlt_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: true, lWinDown: false, rWinDown: false));
    }

    [Fact]
    public void CtrlWin_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: false, lWinDown: true, rWinDown: false));
    }

    [Fact]
    public void CtrlRWin_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: false, altDown: false, lWinDown: false, rWinDown: true));
    }

    [Fact]
    public void CtrlShiftWin_ReturnsFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: false, lWinDown: true, rWinDown: false));
    }

    [Fact]
    public void CtrlShiftWin_AllVariantsReturnFalse()
    {
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: false, lWinDown: false, rWinDown: true));
        Assert.False(_gate.IsGestureModifierActive(ctrlDown: true, shiftDown: true, altDown: false, lWinDown: true, rWinDown: true));
    }
}
