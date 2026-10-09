using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Pure-chord policy: only Ctrl/Shift key-UPs of a pure Ctrl+Shift hold are
/// swallowed (so Windows never sees the layout-switch chord complete).
/// Key-DOWNs always pass; any Alt/Win/other key breaks purity.
/// </summary>
public sealed class LayoutSwitchGuardTests
{
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;
    private const int VkEscape = 0x1B;
    private const int VkLControl = 0xA2;
    private const int VkRShift = 0xA1;
    private const int VkA = 0x41;

    [Fact]
    public void PureChordHeld_BothUpsSwallowed()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        // Key-DOWNs never swallow, even once armed.
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void PureChordReverseOrder_BothUpsSwallowed()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void PureChordLeftRightVariants_BothUpsSwallowed()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkLControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkRShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkLControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkRShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void LoneCtrlTap_NeverSwallowed()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void CtrlShiftEsc_EverythingPassesAfterEsc()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkEscape, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkEscape, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        // The chord is polluted: releases must reach apps/OS untouched.
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void CtrlShiftPlusLetter_EverythingPasses()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkA, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkA, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void AltShift_AllPass()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkMenu, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkMenu, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void CtrlShiftThenAlt_AllReleasesPass()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkMenu, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkMenu, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void GuardOff_PureChordAllPass()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: false));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: false));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: false));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: false));
    }

    [Fact]
    public void DirectionalFocusDisabled_PureChordAllPass()
    {
        var g = new LayoutSwitchGuard();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: false, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: false, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: false, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: false, guardEnabled: true));
    }

    [Fact]
    public void ResyncAfterFullRelease_PollutedChordThenPureChordSwallowedAgain()
    {
        var g = new LayoutSwitchGuard();

        // Polluted chord: Esc breaks purity, releases pass.
        g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkEscape, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkEscape, keyDown: false, directionalFocusEnabled: true, guardEnabled: true);
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));

        // Next gesture resyncs: pure chord is swallowed again.
        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void Reset_ClearsPollution()
    {
        var g = new LayoutSwitchGuard();

        g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkEscape, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Reset();

        Assert.Equal(GuardDecision.Pass, g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Pass, g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void IsChordArmed_PureChordHeld_ReturnsTrue()
    {
        var g = new LayoutSwitchGuard();

        Assert.False(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
        g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.False(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
        g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.True(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void IsChordArmed_AfterOtherKey_ReturnsFalse()
    {
        var g = new LayoutSwitchGuard();

        g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.True(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
        g.Feed(VkA, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.False(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void IsChordArmed_AfterAlt_ReturnsFalse()
    {
        var g = new LayoutSwitchGuard();

        g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.True(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
        g.Feed(VkMenu, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.False(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
    }

    [Fact]
    public void IsChordArmed_FlagsOff_ReturnsFalse()
    {
        var g = new LayoutSwitchGuard();

        g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.True(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
        Assert.False(g.IsChordArmed(directionalFocusEnabled: false, guardEnabled: true));
        Assert.False(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: false));
        Assert.False(g.IsChordArmed(directionalFocusEnabled: false, guardEnabled: false));
    }

    [Fact]
    public void IsChordArmed_DoesNotConsumeEvent()
    {
        var g = new LayoutSwitchGuard();

        g.Feed(VkControl, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        g.Feed(VkShift, keyDown: true, directionalFocusEnabled: true, guardEnabled: true);
        Assert.True(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
        // Probing must not consume: the chord releases are still swallowed.
        Assert.True(g.IsChordArmed(directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkControl, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
        Assert.Equal(GuardDecision.Swallow, g.Feed(VkShift, keyDown: false, directionalFocusEnabled: true, guardEnabled: true));
    }
}
