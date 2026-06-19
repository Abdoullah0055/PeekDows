using System;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class DirectionalFocusGestureDetectorTests
{
    private readonly DirectionalFocusGestureDetector _detector = new();

    [Fact]
    public void BelowThreshold_ReturnsNull()
    {
        var result = _detector.Detect(0, 0, 50, 50, 80);
        Assert.Null(result);
    }

    [Fact]
    public void ExactlyAtThreshold_ReturnsSlot()
    {
        var result = _detector.Detect(0, 0, -60, -60, 80);
        Assert.Equal(DirectionalFocusSlot.TopLeft, result);
    }

    [Fact]
    public void TopLeft_ReturnsTopLeft()
    {
        var result = _detector.Detect(500, 500, 400, 400, 80);
        Assert.Equal(DirectionalFocusSlot.TopLeft, result);
    }

    [Fact]
    public void TopRight_ReturnsTopRight()
    {
        var result = _detector.Detect(500, 500, 600, 400, 80);
        Assert.Equal(DirectionalFocusSlot.TopRight, result);
    }

    [Fact]
    public void BottomLeft_ReturnsBottomLeft()
    {
        var result = _detector.Detect(500, 500, 400, 600, 80);
        Assert.Equal(DirectionalFocusSlot.BottomLeft, result);
    }

    [Fact]
    public void BottomRight_ReturnsBottomRight()
    {
        var result = _detector.Detect(500, 500, 600, 600, 80);
        Assert.Equal(DirectionalFocusSlot.BottomRight, result);
    }

    // --- Non-regression: the four diagonal gestures must keep their corner slots, byte
    // for byte, after the edge-centred slots were added. ---

    [Fact]
    public void DiagonalUpLeft_StillFocusesSlotA_TopLeft()
    {
        var result = _detector.Detect(500, 500, 380, 380, 80);
        Assert.Equal(DirectionalFocusSlot.TopLeft, result);
    }

    [Fact]
    public void DiagonalDownRight_StillFocusesSlotB_BottomRight()
    {
        var result = _detector.Detect(500, 500, 640, 640, 80);
        Assert.Equal(DirectionalFocusSlot.BottomRight, result);
    }

    [Fact]
    public void DiagonalUpRight_StillFocusesSlotC_TopRight()
    {
        var result = _detector.Detect(500, 500, 640, 360, 80);
        Assert.Equal(DirectionalFocusSlot.TopRight, result);
    }

    [Fact]
    public void DiagonalDownLeft_StillFocusesSlotD_BottomLeft()
    {
        var result = _detector.Detect(500, 500, 360, 640, 80);
        Assert.Equal(DirectionalFocusSlot.BottomLeft, result);
    }

    [Fact]
    public void DiagonalAtExactly45Degrees_FallsThroughToDiagonal()
    {
        // At exactly 45° both axes are equal, so neither dominates 2× and it is NOT a
        // cardinal — it resolves as a diagonal corner slot. This guards the boundary
        // between the cardinal and diagonal paths.
        var result = _detector.Detect(0, 0, -100, -100, 80);
        Assert.Equal(DirectionalFocusSlot.TopLeft, result);
    }

    [Fact]
    public void AlmostHorizontal_FocusesMiddleRight()
    {
        // A clearly horizontal move with a small vertical drift: the X axis dominates, so
        // it targets the right edge-centred slot (G) rather than being rejected.
        var result = _detector.Detect(0, 0, 200, 30, 80);
        Assert.Equal(DirectionalFocusSlot.MiddleRight, result);
    }

    [Fact]
    public void AlmostVertical_FocusesBottomCenter()
    {
        // A clearly vertical move with a small horizontal drift: the Y axis dominates, so
        // it targets the bottom edge-centred slot (F).
        var result = _detector.Detect(0, 0, 30, 200, 80);
        Assert.Equal(DirectionalFocusSlot.BottomCenter, result);
    }

    [Fact]
    public void HorizontalOnly_FocusesMiddleRight()
    {
        var result = _detector.Detect(0, 0, 200, 0, 80);
        Assert.Equal(DirectionalFocusSlot.MiddleRight, result);
    }

    [Fact]
    public void HorizontalOnly_FocusesMiddleLeft()
    {
        var result = _detector.Detect(0, 0, -200, 0, 80);
        Assert.Equal(DirectionalFocusSlot.MiddleLeft, result);
    }

    [Fact]
    public void VerticalOnly_FocusesBottomCenter()
    {
        var result = _detector.Detect(0, 0, 0, 200, 80);
        Assert.Equal(DirectionalFocusSlot.BottomCenter, result);
    }

    [Fact]
    public void VerticalOnly_FocusesTopCenter()
    {
        var result = _detector.Detect(0, 0, 0, -200, 80);
        Assert.Equal(DirectionalFocusSlot.TopCenter, result);
    }

    [Fact]
    public void CardinalUp_FocusesSlotE_TopCenter()
    {
        // Ctrl+Shift + mouse up → slot E / haut milieu.
        var result = _detector.Detect(960, 540, 960, 300, 80);
        Assert.Equal(DirectionalFocusSlot.TopCenter, result);
    }

    [Fact]
    public void CardinalDown_FocusesSlotF_BottomCenter()
    {
        // Ctrl+Shift + mouse down → slot F / bas milieu.
        var result = _detector.Detect(960, 540, 960, 800, 80);
        Assert.Equal(DirectionalFocusSlot.BottomCenter, result);
    }

    [Fact]
    public void CardinalRight_FocusesSlotG_MiddleRight()
    {
        // Ctrl+Shift + mouse right → slot G / milieu droite.
        var result = _detector.Detect(960, 540, 1300, 540, 80);
        Assert.Equal(DirectionalFocusSlot.MiddleRight, result);
    }

    [Fact]
    public void CardinalLeft_FocusesSlotH_MiddleLeft()
    {
        // Ctrl+Shift + mouse left → slot H / milieu gauche.
        var result = _detector.Detect(960, 540, 620, 540, 80);
        Assert.Equal(DirectionalFocusSlot.MiddleLeft, result);
    }

    [Fact]
    public void AmbiguousMove_BothAxesPresent_NoDominance_ReturnsNull()
    {
        // Neither axis dominates 2×, and both are above the diagonal minimum on one side
        // only — this is a genuinely ambiguous gesture, so it must be a no-op.
        // dx=50 (≥40 so dxIsDiagonal=true) but dy=30 (<40), and 50 < 30*2=60 → no cardinal.
        var result = _detector.Detect(0, 0, 50, 30, 80);
        Assert.Null(result);
    }

    [Fact]
    public void CardinalDominantAxis_BelowDiagonalMinimumOnOther_StillTriggers()
    {
        // dx=80 clearly dominates a sub-minimum dy=39: a cardinal horizontal gesture.
        // (Pre-8-slot this returned null because cardinals were unsupported.)
        var result = _detector.Detect(0, 0, -80, -39, 80);
        Assert.Equal(DirectionalFocusSlot.MiddleLeft, result);
    }

    [Fact]
    public void CardinalDominantAxisVertical_BelowDiagonalMinimumOnOther_StillTriggers()
    {
        // dy=80 clearly dominates a sub-minimum dx=39: a cardinal vertical gesture.
        var result = _detector.Detect(0, 0, -39, -80, 80);
        Assert.Equal(DirectionalFocusSlot.TopCenter, result);
    }

    [Fact]
    public void CardinalDominantAxis_TooSmall_ReturnsNull()
    {
        // The dominant axis is below the axial minimum (39 < 40), so even a perfectly
        // straight move is too small to be a cardinal gesture.
        var result = _detector.Detect(0, 0, 39, 0, 20);
        Assert.Null(result);
    }

    [Fact]
    public void ExactlyMinAxialPx_TopLeft()
    {
        // Both axes exactly at the diagonal minimum (40) → still a corner diagonal.
        var result = _detector.Detect(0, 0, -40, -40, 50);
        Assert.Equal(DirectionalFocusSlot.TopLeft, result);
    }

    [Fact]
    public void SmallMove_InDeadZone_ReturnsNull()
    {
        // Movement below the configured threshold never triggers any slot — the gesture
        // has not left the anchor zone. This is the "too small" no-op rule.
        var result = _detector.Detect(0, 0, 30, 30, 80);
        Assert.Null(result);
    }

    [Fact]
    public void LargeDiagonalMovement_Works()
    {
        var result = _detector.Detect(960, 540, 100, 0, 80);
        Assert.Equal(DirectionalFocusSlot.TopLeft, result);
    }

    [Fact]
    public void ZeroMovement_ReturnsNull()
    {
        var result = _detector.Detect(500, 500, 500, 500, 80);
        Assert.Null(result);
    }

    [Fact]
    public void UsesConfiguredThreshold_LowerThreshold()
    {
        var result = _detector.Detect(0, 0, 50, 50, 50);
        Assert.Equal(DirectionalFocusSlot.BottomRight, result);
    }

    [Fact]
    public void UsesConfiguredThreshold_HigherThreshold()
    {
        var result = _detector.Detect(0, 0, 50, 50, 100);
        Assert.Null(result);
    }
}
