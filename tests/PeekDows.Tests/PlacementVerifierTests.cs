using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class PlacementVerifierTests
{
    private static WindowPlacement Placement(IntPtr hwnd, Rect rect)
        => new() { Hwnd = hwnd, SlotId = "T", TargetRect = rect };

    private static readonly Rect R = new(0, 0, 640, 1080);

    [Fact]
    public void ExactMatch_NoDeviations()
    {
        var placements = new[] { Placement(new IntPtr(1), R) };

        var dev = PlacementVerifier.FindDeviations(placements, _ => (Rect?)R, _ => false);

        Assert.Empty(dev);
    }

    [Fact]
    public void WithinTolerance_NoDeviations()
    {
        var placements = new[] { Placement(new IntPtr(1), R) };
        var actual = new Rect(2, -3, 641, 1081); // L/T/R/B within 4px (R=643, B=1078)

        var dev = PlacementVerifier.FindDeviations(placements, _ => (Rect?)actual, _ => false);

        Assert.Empty(dev);
    }

    [Fact]
    public void BeyondTolerance_ReportedWithBothRects()
    {
        var placements = new[] { Placement(new IntPtr(7), R) };
        var actual = new Rect(0, 0, 800, 1080); // 160px wider: app clamped min-width

        var dev = PlacementVerifier.FindDeviations(placements, _ => (Rect?)actual, _ => false);

        var single = Assert.Single(dev);
        Assert.Equal(new IntPtr(7), single.Hwnd);
        Assert.Equal(R, single.Expected);
        Assert.Equal(actual, single.Actual);
    }

    [Fact]
    public void MaximizedWindow_Skipped()
    {
        var placements = new[] { Placement(new IntPtr(1), R) };

        var dev = PlacementVerifier.FindDeviations(placements, _ => new Rect(0, 0, 1920, 1080), _ => true);

        Assert.Empty(dev);
    }

    [Fact]
    public void UnreadableWindow_Skipped()
    {
        var placements = new[] { Placement(new IntPtr(1), R) };

        var dev = PlacementVerifier.FindDeviations(placements, _ => (Rect?)null, _ => false);

        Assert.Empty(dev);
    }

    [Fact]
    public void EmptyPlacements_Empty()
    {
        Assert.Empty(PlacementVerifier.FindDeviations(Array.Empty<WindowPlacement>(), _ => (Rect?)R, _ => false));
    }

    [Fact]
    public void ThrowingProbes_NeverThrow()
    {
        var placements = new[] { Placement(new IntPtr(1), R) };

        var dev = PlacementVerifier.FindDeviations(
            placements,
            (Func<IntPtr, Rect?>)(_ => throw new InvalidOperationException("probe")),            (Func<IntPtr, bool>)(_ => throw new InvalidOperationException("probe")));

        Assert.Empty(dev);
    }
}
