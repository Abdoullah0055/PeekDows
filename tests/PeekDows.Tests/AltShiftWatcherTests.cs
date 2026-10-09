using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Alt+Shift chord detector: true exactly on the key-DOWN that completes a
/// clean Alt+Shift coexistence; Ctrl/Win pollute until full release of all
/// 4 families; UPs never fire; repeats never re-fire.
/// </summary>
public sealed class AltShiftWatcherTests
{
    private const int VkMenu = 0x12;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkShift = 0x10;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkControl = 0x11;
    private const int VkLControl = 0xA2;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    [Fact]
    public void AltThenShift_CompletesChord()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.True(w.Feed(VkShift, keyDown: true));
    }

    [Fact]
    public void ShiftThenAlt_CompletesChord()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkShift, keyDown: true));
        Assert.True(w.Feed(VkMenu, keyDown: true));
    }

    [Fact]
    public void LeftRightVariants_CompleteChord()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkLMenu, keyDown: true));
        Assert.True(w.Feed(VkRShift, keyDown: true));
    }

    [Fact]
    public void CtrlHeld_BlocksChord()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkControl, keyDown: true));
        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.False(w.Feed(VkShift, keyDown: true));
    }

    [Fact]
    public void CtrlDownMidHold_PollutesUntilFullRelease()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.False(w.Feed(VkLControl, keyDown: true));
        // Even after Ctrl goes back up, the hold stays polluted.
        Assert.False(w.Feed(VkLControl, keyDown: false));
        Assert.False(w.Feed(VkShift, keyDown: true));

        // Full release of all 4 families re-arms.
        Assert.False(w.Feed(VkShift, keyDown: false));
        Assert.False(w.Feed(VkMenu, keyDown: false));
        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.True(w.Feed(VkShift, keyDown: true));
    }

    [Fact]
    public void WinHeld_BlocksChord()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkLWin, keyDown: true));
        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.False(w.Feed(VkShift, keyDown: true));

        Assert.False(w.Feed(VkShift, keyDown: false));
        Assert.False(w.Feed(VkMenu, keyDown: false));
        Assert.False(w.Feed(VkLWin, keyDown: false));

        Assert.False(w.Feed(VkRMenu, keyDown: true));
        Assert.True(w.Feed(VkLShift, keyDown: true));
    }

    [Fact]
    public void WinDown_StaysPollutedUntilFullRelease()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.False(w.Feed(VkRWin, keyDown: true));
        Assert.False(w.Feed(VkShift, keyDown: true));
        Assert.False(w.Feed(VkRWin, keyDown: false));
        // Shift still held from a polluted hold: no fire.
        Assert.False(w.Feed(VkMenu, keyDown: false));
        Assert.False(w.Feed(VkShift, keyDown: false));

        Assert.False(w.Feed(VkShift, keyDown: true));
        Assert.True(w.Feed(VkMenu, keyDown: true));
    }

    [Fact]
    public void KeyUps_Alone_ReturnFalse()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkMenu, keyDown: false));
        Assert.False(w.Feed(VkShift, keyDown: false));
        Assert.False(w.Feed(VkControl, keyDown: false));
        Assert.False(w.Feed(VkLWin, keyDown: false));
    }

    [Fact]
    public void RepeatedDown_SecondReturnsFalse()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.True(w.Feed(VkShift, keyDown: true));
        // Auto-repeat / duplicate DOWN while already coexisting: no re-fire.
        Assert.False(w.Feed(VkShift, keyDown: true));
        Assert.False(w.Feed(VkLShift, keyDown: true));
        Assert.False(w.Feed(VkMenu, keyDown: true));
    }

    [Fact]
    public void FullRelease_RearmsNewChord()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.True(w.Feed(VkShift, keyDown: true));
        Assert.False(w.Feed(VkShift, keyDown: false));
        Assert.False(w.Feed(VkMenu, keyDown: false));

        Assert.False(w.Feed(VkShift, keyDown: true));
        Assert.True(w.Feed(VkMenu, keyDown: true));
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkMenu, keyDown: true));
        w.Reset();
        // Alt was cleared: lone Shift cannot complete.
        Assert.False(w.Feed(VkShift, keyDown: true));

        w.Reset();
        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.True(w.Feed(VkShift, keyDown: true));
    }

    [Fact]
    public void Reset_ClearsPollution()
    {
        var w = new AltShiftWatcher();

        Assert.False(w.Feed(VkControl, keyDown: true));
        w.Reset();
        Assert.False(w.Feed(VkMenu, keyDown: true));
        Assert.True(w.Feed(VkShift, keyDown: true));
    }
}
