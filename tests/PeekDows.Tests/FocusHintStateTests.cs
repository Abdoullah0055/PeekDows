using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class FocusHintStateTests
{
    // Matrix: 2 modes (Off, Overlay) x slot null/present x hasTarget true/false.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Off_NullSlot_HidesOverlay(bool hasTarget)
    {
        var vm = FocusHintState.Resolve("Off", null, hasTarget);

        Assert.False(vm.ShowOverlay);
        Assert.Null(vm.ActiveSlot);
        Assert.Equal(hasTarget, vm.HasTarget);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Off_WithSlot_HidesOverlay(bool hasTarget)
    {
        var vm = FocusHintState.Resolve("Off", DirectionalFocusSlot.TopLeft, hasTarget);

        Assert.False(vm.ShowOverlay);
        Assert.Equal(DirectionalFocusSlot.TopLeft, vm.ActiveSlot);
        Assert.Equal(hasTarget, vm.HasTarget);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Overlay_NullSlot_ShowsOverlay(bool hasTarget)
    {
        // Gesture active with no slot yet: overlay still shows (caller hides when empty).
        var vm = FocusHintState.Resolve("Overlay", null, hasTarget);

        Assert.True(vm.ShowOverlay);
        Assert.Null(vm.ActiveSlot);
        Assert.Equal(hasTarget, vm.HasTarget);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Overlay_WithSlot_ShowsOverlay(bool hasTarget)
    {
        var vm = FocusHintState.Resolve("Overlay", DirectionalFocusSlot.BottomRight, hasTarget);

        Assert.True(vm.ShowOverlay);
        Assert.Equal(DirectionalFocusSlot.BottomRight, vm.ActiveSlot);
        Assert.Equal(hasTarget, vm.HasTarget);
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("OFF", false)]
    [InlineData("  Off  ", false)]
    [InlineData("overlay", true)]
    [InlineData("OVERLAY", true)]
    [InlineData("  Overlay  ", true)]
    public void Mode_IsNormalizedCaseInsensitive(string mode, bool expectedShow)
    {
        var vm = FocusHintState.Resolve(mode, DirectionalFocusSlot.TopLeft, true);

        Assert.Equal(expectedShow, vm.ShowOverlay);
    }

    // Backward compat: pre-V5 values collapse onto Off|Overlay.

    [Theory]
    [InlineData("Both")]
    [InlineData("both")]
    [InlineData("BOTH")]
    [InlineData("Spotlight")]
    [InlineData("SPOTLIGHT")]
    [InlineData("spotlight")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Banana")]
    [InlineData("full")]
    public void LegacyOrUnknownMode_FallsBackToOverlay(string? mode)
    {
        var withSlot = FocusHintState.Resolve(mode, DirectionalFocusSlot.TopLeft, true);
        var nullSlot = FocusHintState.Resolve(mode, null, false);

        Assert.True(withSlot.ShowOverlay);
        Assert.Equal(DirectionalFocusSlot.TopLeft, withSlot.ActiveSlot);
        Assert.True(withSlot.HasTarget);

        Assert.True(nullSlot.ShowOverlay);
        Assert.Null(nullSlot.ActiveSlot);
        Assert.False(nullSlot.HasTarget);
    }

    [Fact]
    public void Passthrough_PreservesSlotAndHasTarget()
    {
        var vm = FocusHintState.Resolve("Overlay", DirectionalFocusSlot.TopRight, true);

        Assert.Equal(DirectionalFocusSlot.TopRight, vm.ActiveSlot);
        Assert.True(vm.HasTarget);
    }
}
