using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Agent F2 — Layouts Halves/Thirds (LayoutEngine exclusif).
/// Halves/Thirds assignent DANS L'ORDRE D'ENTRÉE (aucun re-tri interne) pour que
/// les pins F3 atterrissent en premier; Grid garde son tri prioritaire.
/// </summary>
public sealed class LayoutModesTests
{
    private readonly LayoutEngine _engine = new();
    private static readonly Rect WorkArea1920 = new(0, 0, 1920, 1080);

    private static ManagedWindow Win(int hwnd, bool pinned = false, bool foreground = false) =>
        new()
        {
            Hwnd = (IntPtr)hwnd,
            IsVisible = true,
            IsPinned = pinned,
            IsForeground = foreground,
            FirstSeenAt = new DateTime(2026, 1, 1).AddSeconds(hwnd)
        };

    private static List<ManagedWindow> Windows(params ManagedWindow[] wins) => wins.ToList();

    // ---------- Halves ----------

    [Fact]
    public void Halves_TwoWindows_ExactRects1920x1080()
    {
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(1), Win(2)), WorkArea1920, new AppSettings());

        Assert.Equal(2, placements.Count);

        Assert.Equal("Left", placements[0].SlotId);
        Assert.Equal(new Rect(0, 0, 960, 1080), placements[0].TargetRect);
        Assert.Equal(0, placements[0].TargetRect.Left);
        Assert.Equal(0, placements[0].TargetRect.Top);
        Assert.Equal(960, placements[0].TargetRect.Width);
        Assert.Equal(1080, placements[0].TargetRect.Height);

        Assert.Equal("Right", placements[1].SlotId);
        Assert.Equal(new Rect(960, 0, 960, 1080), placements[1].TargetRect);
        Assert.Equal(960, placements[1].TargetRect.Left);
        Assert.Equal(0, placements[1].TargetRect.Top);
        Assert.Equal(960, placements[1].TargetRect.Width);
        Assert.Equal(1080, placements[1].TargetRect.Height);

        Assert.All(placements, p => Assert.False(p.BringToFront));
    }

    [Fact]
    public void Halves_OneWindow_TakesLeftHalfOnly()
    {
        // Le lone-maximize natif reste géré par la policy en amont; le moteur
        // place une fenêtre seule sur la moitié gauche, pas en plein écran.
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(1)), WorkArea1920, new AppSettings());

        Assert.Single(placements);
        Assert.Equal("Left", placements[0].SlotId);
        Assert.Equal(new Rect(0, 0, 960, 1080), placements[0].TargetRect);
    }

    [Fact]
    public void Halves_Empty_ReturnsEmpty()
    {
        Assert.Empty(_engine.CalculateHalvesPlacements([], WorkArea1920, new AppSettings()));
    }

    [Fact]
    public void Halves_PreservesInputOrder_NoInternalResort()
    {
        // Fenêtre pinnée DERNIÈRE en entrée → reste dernière (slot Right).
        // Grid la remonterait en premier; Halves ne re-trie jamais.
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(3), Win(1, pinned: true)), WorkArea1920, new AppSettings());

        Assert.Equal(new[] { (IntPtr)3, (IntPtr)1 }, placements.Select(p => p.Hwnd).ToArray());
        Assert.Equal(new[] { "Left", "Right" }, placements.Select(p => p.SlotId).ToArray());
    }

    [Fact]
    public void Halves_ForegroundLast_StaysLast()
    {
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(5), Win(2, foreground: true)), WorkArea1920, new AppSettings());

        Assert.Equal(new[] { (IntPtr)5, (IntPtr)2 }, placements.Select(p => p.Hwnd).ToArray());
        Assert.Equal(new[] { "Left", "Right" }, placements.Select(p => p.SlotId).ToArray());
    }

    [Fact]
    public void Halves_ThreeWindows_CyclesLeftRightLeft()
    {
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(1), Win(2), Win(3)), WorkArea1920, new AppSettings());

        Assert.Equal(3, placements.Count);
        Assert.Equal(new[] { "Left", "Right", "Left" }, placements.Select(p => p.SlotId).ToArray());
        Assert.Equal(new[] { (IntPtr)1, (IntPtr)2, (IntPtr)3 }, placements.Select(p => p.Hwnd).ToArray());
    }

    [Fact]
    public void Halves_FiveWindows_CyclesModulo()
    {
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(1), Win(2), Win(3), Win(4), Win(5)), WorkArea1920, new AppSettings());

        Assert.Equal(5, placements.Count);
        Assert.Equal(
            new[] { "Left", "Right", "Left", "Right", "Left" },
            placements.Select(p => p.SlotId).ToArray());
    }

    [Fact]
    public void Halves_RespectsWorkAreaOffset()
    {
        var workArea = new Rect(0, 40, 1920, 1040);
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(1), Win(2)), workArea, new AppSettings());

        Assert.Equal(new Rect(0, 40, 960, 1040), placements[0].TargetRect);
        Assert.Equal(new Rect(960, 40, 960, 1040), placements[1].TargetRect);
    }

    [Fact]
    public void Halves_OddWidth_NoGapNoOverflow()
    {
        var workArea = new Rect(0, 0, 1919, 1080);
        var placements = _engine.CalculateHalvesPlacements(
            Windows(Win(1), Win(2)), workArea, new AppSettings());

        Assert.Equal(959, placements[0].TargetRect.Width);
        Assert.Equal(960, placements[1].TargetRect.Width);
        Assert.Equal(placements[0].TargetRect.Right, placements[1].TargetRect.Left);
        Assert.Equal(workArea.Right, placements[1].TargetRect.Right);
    }

    // ---------- Thirds ----------

    [Fact]
    public void Thirds_ThreeWindows_ExactRects1920x1080()
    {
        var placements = _engine.CalculateThirdsPlacements(
            Windows(Win(1), Win(2), Win(3)), WorkArea1920, new AppSettings());

        Assert.Equal(3, placements.Count);

        Assert.Equal("Col1", placements[0].SlotId);
        Assert.Equal(new Rect(0, 0, 640, 1080), placements[0].TargetRect);

        Assert.Equal("Col2", placements[1].SlotId);
        Assert.Equal(new Rect(640, 0, 640, 1080), placements[1].TargetRect);

        Assert.Equal("Col3", placements[2].SlotId);
        Assert.Equal(new Rect(1280, 0, 640, 1080), placements[2].TargetRect);

        // Pleine hauteur + contiguïté + aucun dépassement.
        foreach (var p in placements)
        {
            Assert.Equal(0, p.TargetRect.Top);
            Assert.Equal(1080, p.TargetRect.Height);
            Assert.False(p.BringToFront);
        }
        Assert.Equal(placements[0].TargetRect.Right, placements[1].TargetRect.Left);
        Assert.Equal(placements[1].TargetRect.Right, placements[2].TargetRect.Left);
        Assert.Equal(WorkArea1920.Right, placements[2].TargetRect.Right);
        Assert.Equal(new[] { (IntPtr)1, (IntPtr)2, (IntPtr)3 }, placements.Select(p => p.Hwnd).ToArray());
    }

    [Fact]
    public void Thirds_OneWindow_TakesCol1Only()
    {
        var placements = _engine.CalculateThirdsPlacements(
            Windows(Win(1)), WorkArea1920, new AppSettings());

        Assert.Single(placements);
        Assert.Equal("Col1", placements[0].SlotId);
        Assert.Equal(new Rect(0, 0, 640, 1080), placements[0].TargetRect);
    }

    [Fact]
    public void Thirds_Empty_ReturnsEmpty()
    {
        Assert.Empty(_engine.CalculateThirdsPlacements([], WorkArea1920, new AppSettings()));
    }

    [Fact]
    public void Thirds_PreservesInputOrder_NoInternalResort()
    {
        var placements = _engine.CalculateThirdsPlacements(
            Windows(Win(7), Win(8), Win(9, pinned: true)), WorkArea1920, new AppSettings());

        Assert.Equal(
            new[] { (IntPtr)7, (IntPtr)8, (IntPtr)9 },
            placements.Select(p => p.Hwnd).ToArray());
        Assert.Equal(
            new[] { "Col1", "Col2", "Col3" },
            placements.Select(p => p.SlotId).ToArray());
    }

    [Fact]
    public void Thirds_FourWindows_CyclesModulo()
    {
        var placements = _engine.CalculateThirdsPlacements(
            Windows(Win(1), Win(2), Win(3), Win(4)), WorkArea1920, new AppSettings());

        Assert.Equal(4, placements.Count);
        Assert.Equal(
            new[] { "Col1", "Col2", "Col3", "Col1" },
            placements.Select(p => p.SlotId).ToArray());
    }

    [Fact]
    public void Thirds_NonDivisibleWidth_LastColumnAbsorbsRemainder()
    {
        var workArea = new Rect(0, 0, 1366, 768);
        var placements = _engine.CalculateThirdsPlacements(
            Windows(Win(1), Win(2), Win(3)), workArea, new AppSettings());

        Assert.Equal(455, placements[0].TargetRect.Width);
        Assert.Equal(455, placements[1].TargetRect.Width);
        Assert.Equal(456, placements[2].TargetRect.Width);
        Assert.Equal(placements[0].TargetRect.Right, placements[1].TargetRect.Left);
        Assert.Equal(placements[1].TargetRect.Right, placements[2].TargetRect.Left);
        Assert.Equal(workArea.Right, placements[2].TargetRect.Right);
        Assert.All(placements, p => Assert.Equal(768, p.TargetRect.Height));
    }

    // ---------- Dispatcher ----------

    public static IEnumerable<object?[]> FallbackModes() =>
        new List<object?[]>
        {
            new object?[] { null },
            new object?[] { "" },
            new object?[] { "   " },
            new object?[] { "Yolo" },
            new object?[] { "Grid" },
            new object?[] { "GRID" },
        };

    [Theory]
    [MemberData(nameof(FallbackModes))]
    public void Dispatcher_NullEmptyUnknownOrGrid_FallsBackToClassicPeekGrid(string? mode)
    {
        var settings = new AppSettings();
        var windows = Windows(Win(1), Win(2), Win(3));

        var actual = _engine.CalculatePlacements(mode, windows, WorkArea1920, settings);
        var expected = _engine.CalculateClassicPeekGridPlacements(windows, WorkArea1920, settings);

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Select(p => p.Hwnd).ToArray(), actual.Select(p => p.Hwnd).ToArray());
        Assert.Equal(expected.Select(p => p.SlotId).ToArray(), actual.Select(p => p.SlotId).ToArray());
        Assert.Equal(expected.Select(p => p.TargetRect).ToArray(), actual.Select(p => p.TargetRect).ToArray());
    }

    [Theory]
    [InlineData("halves")]
    [InlineData("Halves")]
    [InlineData("HALVES")]
    [InlineData(" halves ")]
    public void Dispatcher_Halves_IsCaseInsensitive(string mode)
    {
        var settings = new AppSettings();
        var windows = Windows(Win(1), Win(2), Win(3));

        var actual = _engine.CalculatePlacements(mode, windows, WorkArea1920, settings);
        var expected = _engine.CalculateHalvesPlacements(windows, WorkArea1920, settings);

        Assert.Equal(expected.Select(p => p.SlotId).ToArray(), actual.Select(p => p.SlotId).ToArray());
        Assert.Equal(expected.Select(p => p.TargetRect).ToArray(), actual.Select(p => p.TargetRect).ToArray());
    }

    [Theory]
    [InlineData("thirds")]
    [InlineData("Thirds")]
    [InlineData("THIRDS")]
    [InlineData(" thirds ")]
    public void Dispatcher_Thirds_IsCaseInsensitive(string mode)
    {
        var settings = new AppSettings();
        var windows = Windows(Win(1), Win(2), Win(3), Win(4));

        var actual = _engine.CalculatePlacements(mode, windows, WorkArea1920, settings);
        var expected = _engine.CalculateThirdsPlacements(windows, WorkArea1920, settings);

        Assert.Equal(expected.Select(p => p.SlotId).ToArray(), actual.Select(p => p.SlotId).ToArray());
        Assert.Equal(expected.Select(p => p.TargetRect).ToArray(), actual.Select(p => p.TargetRect).ToArray());
    }

    [Fact]
    public void Dispatcher_EmptyWindows_ReturnsEmptyWhateverMode()
    {
        var settings = new AppSettings();
        Assert.Empty(_engine.CalculatePlacements("Halves", [], WorkArea1920, settings));
        Assert.Empty(_engine.CalculatePlacements("Thirds", [], WorkArea1920, settings));
        Assert.Empty(_engine.CalculatePlacements(null, [], WorkArea1920, settings));
    }
}
