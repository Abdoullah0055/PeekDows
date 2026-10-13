using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class FrameCompensationTests
{
    [Fact]
    public void Measure_SevenPixelBorders_ReturnsMargins()
    {
        var outer = new Rect(0, 0, 640, 1080);
        var visible = new Rect(7, 7, 626, 1066); // 640-14, 1080-14

        var m = FrameCompensation.Measure(outer, visible);

        Assert.Equal(new FrameMargins(7, 7, 7, 7), m);
    }

    [Fact]
    public void Measure_BorderlessWindow_ReturnsZero()
    {
        var r = new Rect(0, 0, 640, 1080);

        Assert.Equal(FrameMargins.Zero, FrameCompensation.Measure(r, r));
    }

    [Fact]
    public void Measure_NegativeDiffs_ClampToZero()
    {
        var outer = new Rect(0, 0, 100, 100);
        var visible = new Rect(-2, -2, 104, 104);

        Assert.Equal(FrameMargins.Zero, FrameCompensation.Measure(outer, visible));
    }

    [Fact]
    public void Compensate_InflatesSoVisibleLandsOnTarget()
    {
        var target = new Rect(640, 0, 640, 1080);
        var margins = new FrameMargins(7, 7, 7, 7);

        var outer = FrameCompensation.Compensate(target, margins);

        Assert.Equal(new Rect(633, -7, 654, 1094), outer);
        // Visible area of the inflated outer rect == the slot exactly.
        var visible = new Rect(
            outer.Left + margins.Left,
            outer.Top + margins.Top,
            outer.Width - margins.Left - margins.Right,
            outer.Height - margins.Top - margins.Bottom);
        Assert.Equal(target, visible);
    }

    [Fact]
    public void Compensate_ZeroMargins_Unchanged()
    {
        var target = new Rect(0, 0, 640, 1080);

        Assert.Equal(target, FrameCompensation.Compensate(target, FrameMargins.Zero));
    }

    [Fact]
    public void WithCompensatedRects_MaximizeKind_Untouched()
    {
        var placements = new[]
        {
            new WindowPlacement { Hwnd = new IntPtr(1), SlotId = "Maximize", TargetRect = new Rect(0, 0, 1920, 1080), Kind = PlacementKind.Maximize },
        };
        var margins = new Dictionary<IntPtr, FrameMargins> { [new IntPtr(1)] = new FrameMargins(7, 7, 7, 7) };

        var result = FrameCompensation.WithCompensatedRects(placements, margins);

        Assert.Equal(new Rect(0, 0, 1920, 1080), Assert.Single(result).TargetRect);
    }

    [Fact]
    public void WithCompensatedRects_MissingEntry_Unchanged()
    {
        var placements = new[]
        {
            new WindowPlacement { Hwnd = new IntPtr(2), SlotId = "Col1", TargetRect = new Rect(0, 0, 640, 1080), Kind = PlacementKind.Reposition },
        };

        var result = FrameCompensation.WithCompensatedRects(placements, new Dictionary<IntPtr, FrameMargins>());

        Assert.Equal(new Rect(0, 0, 640, 1080), Assert.Single(result).TargetRect);
    }

    [Fact]
    public void WithCompensatedRects_Reposition_InflatedPreservingOtherProps()
    {
        var placements = new[]
        {
            new WindowPlacement { Hwnd = new IntPtr(3), SlotId = "Col2", TargetRect = new Rect(640, 0, 640, 1080), Kind = PlacementKind.Reposition, BringToFront = true },
        };
        var margins = new Dictionary<IntPtr, FrameMargins> { [new IntPtr(3)] = new FrameMargins(7, 0, 7, 0) };

        var single = Assert.Single(FrameCompensation.WithCompensatedRects(placements, margins));

        Assert.Equal(new Rect(633, 0, 654, 1080), single.TargetRect);
        Assert.Equal("Col2", single.SlotId);
        Assert.True(single.BringToFront);
        Assert.Equal(new IntPtr(3), single.Hwnd);
    }
}
