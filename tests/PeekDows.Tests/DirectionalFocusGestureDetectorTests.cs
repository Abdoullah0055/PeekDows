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

    [Fact]
    public void AlmostHorizontal_ReturnsNull()
    {
        var result = _detector.Detect(0, 0, 200, 30, 80);
        Assert.Null(result);
    }

    [Fact]
    public void AlmostVertical_ReturnsNull()
    {
        var result = _detector.Detect(0, 0, 30, 200, 80);
        Assert.Null(result);
    }

    [Fact]
    public void HorizontalOnly_ReturnsNull()
    {
        var result = _detector.Detect(0, 0, 200, 0, 80);
        Assert.Null(result);
    }

    [Fact]
    public void VerticalOnly_ReturnsNull()
    {
        var result = _detector.Detect(0, 0, 0, 200, 80);
        Assert.Null(result);
    }

    [Fact]
    public void ExactlyMinAxialPx_TopLeft()
    {
        var result = _detector.Detect(0, 0, -40, -40, 50);
        Assert.Equal(DirectionalFocusSlot.TopLeft, result);
    }

    [Fact]
    public void OneBelowMinAxialPxDx_ReturnsNull()
    {
        var result = _detector.Detect(0, 0, -39, -80, 80);
        Assert.Null(result);
    }

    [Fact]
    public void OneBelowMinAxialPxDy_ReturnsNull()
    {
        var result = _detector.Detect(0, 0, -80, -39, 80);
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
