using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class LayoutEngineTests
{
    private readonly LayoutEngine _engine = new();

    public static IEnumerable<object[]> GetResolutions()
    {
        yield return new object[] { new Rect(0, 0, 1366, 768) };
        yield return new object[] { new Rect(0, 0, 1440, 900) };
        yield return new object[] { new Rect(0, 0, 1536, 864) };
        yield return new object[] { new Rect(0, 0, 1920, 1080) };
        yield return new object[] { new Rect(0, 0, 2560, 1440) };
        yield return new object[] { new Rect(0, 40, 1920, 1040) };
        yield return new object[] { new Rect(0, 0, 1919, 1079) };
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void Slots_DoNotExceedWorkArea(Rect workArea)
    {
        var grid = _engine.CreateDefaultGrid(workArea);
        var slots = _engine.CreateDefaultSlots(grid);

        foreach (var slot in slots)
        {
            var rect = _engine.CalculateSlotRect(slot, grid);

            Assert.True(rect.Left >= workArea.Left, $"Slot {slot.Id} left out of bounds");
            Assert.True(rect.Top >= workArea.Top, $"Slot {slot.Id} top out of bounds");
            Assert.True(rect.Right <= workArea.Right, $"Slot {slot.Id} right out of bounds (Right {rect.Right} > {workArea.Right})");
            Assert.True(rect.Bottom <= workArea.Bottom, $"Slot {slot.Id} bottom out of bounds (Bottom {rect.Bottom} > {workArea.Bottom})");
        }
    }

    [Fact]
    public void CreateDefaultGrid_4Columns_3Rows()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var grid = _engine.CreateDefaultGrid(workArea);

        Assert.Equal(4, grid.Columns);
        Assert.Equal(3, grid.Rows);
        Assert.Equal(480, grid.CellWidth);
        Assert.Equal(360, grid.CellHeight);
    }

    [Fact]
    public void CreateDefaultSlots_Has4Slots()
    {
        var grid = _engine.CreateDefaultGrid(new Rect(0, 0, 1920, 1080));
        var slots = _engine.CreateDefaultSlots(grid);

        Assert.Equal(4, slots.Count);
        Assert.Equal("A", slots[0].Id);
        Assert.Equal("B", slots[1].Id);
        Assert.Equal("C", slots[2].Id);
        Assert.Equal("D", slots[3].Id);
    }

    [Fact]
    public void SlotA_CellNumbers()
    {
        var grid = _engine.CreateDefaultGrid(new Rect(0, 0, 1920, 1080));
        var slots = _engine.CreateDefaultSlots(grid);
        var slotA = slots.First(s => s.Id == "A");

        Assert.Equal(new[] { 1, 2, 3, 5, 6, 7 }, slotA.CellNumbers);
    }

    [Fact]
    public void SlotB_CellNumbers()
    {
        var grid = _engine.CreateDefaultGrid(new Rect(0, 0, 1920, 1080));
        var slots = _engine.CreateDefaultSlots(grid);
        var slotB = slots.First(s => s.Id == "B");

        Assert.Equal(new[] { 6, 7, 8, 10, 11, 12 }, slotB.CellNumbers);
    }

    [Fact]
    public void SlotC_CellNumbers()
    {
        var grid = _engine.CreateDefaultGrid(new Rect(0, 0, 1920, 1080));
        var slots = _engine.CreateDefaultSlots(grid);
        var slotC = slots.First(s => s.Id == "C");

        Assert.Equal(new[] { 2, 3, 4, 6, 7, 8 }, slotC.CellNumbers);
    }

    [Fact]
    public void SlotD_CellNumbers()
    {
        var grid = _engine.CreateDefaultGrid(new Rect(0, 0, 1920, 1080));
        var slots = _engine.CreateDefaultSlots(grid);
        var slotD = slots.First(s => s.Id == "D");

        Assert.Equal(new[] { 5, 6, 7, 9, 10, 11 }, slotD.CellNumbers);
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void SlotA_Rect_TopLeft3Cols2Rows(Rect workArea)
    {
        var grid = _engine.CreateDefaultGrid(workArea);
        var slot = _engine.CreateDefaultSlots(grid).First(s => s.Id == "A");
        var rect = _engine.CalculateSlotRect(slot, grid);

        Assert.Equal(workArea.Left, rect.Left);
        Assert.Equal(workArea.Top, rect.Top);
        Assert.Equal(grid.CellWidth * 3, rect.Width);
        Assert.Equal(grid.CellHeight * 2, rect.Height);
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void SlotB_Rect_BottomRight3Cols2Rows(Rect workArea)
    {
        var grid = _engine.CreateDefaultGrid(workArea);
        var slot = _engine.CreateDefaultSlots(grid).First(s => s.Id == "B");
        var rect = _engine.CalculateSlotRect(slot, grid);

        Assert.Equal(workArea.Left + grid.CellWidth, rect.Left);
        Assert.Equal(workArea.Top + grid.CellHeight, rect.Top);
        Assert.Equal(workArea.Right - rect.Left, rect.Width);
        Assert.Equal(workArea.Bottom - rect.Top, rect.Height);
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void SlotC_Rect_TopRight3Cols2Rows(Rect workArea)
    {
        var grid = _engine.CreateDefaultGrid(workArea);
        var slot = _engine.CreateDefaultSlots(grid).First(s => s.Id == "C");
        var rect = _engine.CalculateSlotRect(slot, grid);

        Assert.Equal(workArea.Left + grid.CellWidth, rect.Left);
        Assert.Equal(workArea.Top, rect.Top);
        Assert.Equal(workArea.Right - rect.Left, rect.Width);
        Assert.Equal(grid.CellHeight * 2, rect.Height);
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void SlotD_Rect_BottomLeft3Cols2Rows(Rect workArea)
    {
        var grid = _engine.CreateDefaultGrid(workArea);
        var slot = _engine.CreateDefaultSlots(grid).First(s => s.Id == "D");
        var rect = _engine.CalculateSlotRect(slot, grid);

        Assert.Equal(workArea.Left, rect.Left);
        Assert.Equal(workArea.Top + grid.CellHeight, rect.Top);
        Assert.Equal(grid.CellWidth * 3, rect.Width);
        Assert.Equal(workArea.Bottom - rect.Top, rect.Height);
    }

    [Fact]
    public void CalculatePlacements_0Windows_EmptyResult()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var placements = _engine.CalculatePlacements([], workArea, settings);

        Assert.Empty(placements);
    }

    [Fact]
    public void CalculatePlacements_1Window_TakesSlotA()
    {
        // The lone-window maximize decision is made per monitor by
        // MultiMonitorLayoutService + LoneWindowMaximizePolicy; the engine itself has no
        // special single-window mode anymore.
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow> { new() { Hwnd = (IntPtr)1, IsVisible = true, FirstSeenAt = DateTime.Now } };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Single(placements);
        Assert.Equal("A", placements[0].SlotId);
    }

    [Fact]
    public void CalculatePlacements_2Windows_A_and_B()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Equal(2, placements.Count);
        Assert.Equal("A", placements[0].SlotId);
        Assert.Equal("B", placements[1].SlotId);
    }

    [Fact]
    public void CalculatePlacements_3Windows_A_B_C()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Equal(3, placements.Count);
        Assert.Equal("A", placements[0].SlotId);
        Assert.Equal("B", placements[1].SlotId);
        Assert.Equal("C", placements[2].SlotId);
    }

    [Fact]
    public void CalculatePlacements_4Windows_A_B_C_D()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Equal(4, placements.Count);
        Assert.Equal("A", placements[0].SlotId);
        Assert.Equal("B", placements[1].SlotId);
        Assert.Equal("C", placements[2].SlotId);
        Assert.Equal("D", placements[3].SlotId);
    }

    [Fact]
    public void CalculatePlacements_5Windows_KeepsOnly4()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>();
        for (int i = 1; i <= 5; i++)
        {
            windows.Add(new ManagedWindow { Hwnd = (IntPtr)i, FirstSeenAt = DateTime.Now.AddMinutes(-i) });
        }

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Equal(4, placements.Count);
        Assert.Contains(placements, p => p.SlotId == "A");
        Assert.Contains(placements, p => p.SlotId == "B");
        Assert.Contains(placements, p => p.SlotId == "C");
        Assert.Contains(placements, p => p.SlotId == "D");
    }

    [Fact]
    public void CalculatePlacements_5Windows_ReturnsOnly4Placements_A_B_C_D()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var now = DateTime.Now;
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, IsPinned = true, FirstSeenAt = now },
            new() { Hwnd = (IntPtr)2, IsForeground = true, FirstSeenAt = now, LastFocusedAt = now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = now },
            new() { Hwnd = (IntPtr)5, FirstSeenAt = now.AddHours(-1) }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Equal(4, placements.Count);
        var slotIds = placements.Select(p => p.SlotId).ToList();
        Assert.Contains("A", slotIds);
        Assert.Contains("B", slotIds);
        Assert.Contains("C", slotIds);
        Assert.Contains("D", slotIds);
        Assert.DoesNotContain(placements, p => p.Hwnd == (IntPtr)5);
    }

    [Fact]
    public void CalculatePlacements_NonDivisible_1366x768()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1366, 768);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        foreach (var p in placements)
        {
            Assert.True(p.TargetRect.Right <= workArea.Right, $"Slot {p.SlotId} right {p.TargetRect.Right} exceeds {workArea.Right}");
            Assert.True(p.TargetRect.Bottom <= workArea.Bottom, $"Slot {p.SlotId} bottom {p.TargetRect.Bottom} exceeds {workArea.Bottom}");
        }
    }

    [Fact]
    public void CalculatePlacements_WithTaskbar_WorkAreaSmaller()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1040);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Equal(2, placements.Count);
        foreach (var p in placements)
        {
            Assert.True(p.TargetRect.Bottom <= workArea.Bottom);
        }
    }

    [Fact]
    public void Priority_PinnedBeforeForeground()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, IsForeground = true, FirstSeenAt = DateTime.Now, LastFocusedAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, IsPinned = true, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Equal("A", placements[0].SlotId);
        Assert.Equal((IntPtr)2, placements[0].Hwnd);
    }

    [Fact]
    public void NoDuplicateSlotAssignments()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);
        var slotIds = placements.Select(p => p.SlotId).ToList();

        Assert.Equal(slotIds.Distinct().Count(), slotIds.Count);
    }

    [Fact]
    public void ClassicPeekGrid_1280x720_ReturnsExpectedRects()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.Equal(4, placements.Count);

        var a = placements.First(p => p.SlotId == "A");
        Assert.Equal(0, a.TargetRect.Left);
        Assert.Equal(0, a.TargetRect.Top);
        Assert.Equal(1152, a.TargetRect.Width);
        Assert.Equal(648, a.TargetRect.Height);

        var b = placements.First(p => p.SlotId == "B");
        Assert.Equal(128, b.TargetRect.Left);
        Assert.Equal(72, b.TargetRect.Top);
        Assert.Equal(1152, b.TargetRect.Width);
        Assert.Equal(648, b.TargetRect.Height);

        var c = placements.First(p => p.SlotId == "C");
        Assert.Equal(128, c.TargetRect.Left);
        Assert.Equal(0, c.TargetRect.Top);
        Assert.Equal(1152, c.TargetRect.Width);
        Assert.Equal(648, c.TargetRect.Height);

        var d = placements.First(p => p.SlotId == "D");
        Assert.Equal(0, d.TargetRect.Left);
        Assert.Equal(72, d.TargetRect.Top);
        Assert.Equal(1152, d.TargetRect.Width);
        Assert.Equal(648, d.TargetRect.Height);
    }

    [Fact]
    public void ClassicPeekGrid_UsesNinetyPercentWidthAndHeight()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);
        var a = placements.First(p => p.SlotId == "A");

        Assert.Equal((int)Math.Round(workArea.Width * 0.90), a.TargetRect.Width);
        Assert.Equal((int)Math.Round(workArea.Height * 0.90), a.TargetRect.Height);
    }

    [Fact]
    public void ClassicPeekGrid_LeavesTenPercentVisibleStrips()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        var a = placements.First(p => p.SlotId == "A");
        Assert.Equal(128, workArea.Right - a.TargetRect.Right);
        Assert.Equal(72, workArea.Bottom - a.TargetRect.Bottom);

        var b = placements.First(p => p.SlotId == "B");
        Assert.Equal(128, b.TargetRect.Left - workArea.Left);
        Assert.Equal(72, b.TargetRect.Top - workArea.Top);
    }

    [Fact]
    public void ClassicPeekGrid_BottomSlots_AreAnchoredToBottom()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        var b = placements.First(p => p.SlotId == "B");
        Assert.Equal(workArea.Bottom, b.TargetRect.Bottom);

        var d = placements.First(p => p.SlotId == "D");
        Assert.Equal(workArea.Bottom, d.TargetRect.Bottom);
    }

    [Fact]
    public void ClassicPeekGrid_RightSlots_AreAnchoredToRight()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        var b = placements.First(p => p.SlotId == "B");
        Assert.Equal(workArea.Right, b.TargetRect.Right);

        var c = placements.First(p => p.SlotId == "C");
        Assert.Equal(workArea.Right, c.TargetRect.Right);
    }

    [Fact]
    public void ClassicPeekGrid_NoForcedFocusBringToFront()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.All(placements, p => Assert.False(p.BringToFront));
    }

    [Fact]
    public void ClassicPeekGrid_NonDivisibleWorkArea_StillUsesNinetyPercentAndAnchorsEdges()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1366, 768);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        var a = placements.First(p => p.SlotId == "A");
        Assert.Equal((int)Math.Round(1366 * 0.90), a.TargetRect.Width);
        Assert.Equal((int)Math.Round(768 * 0.90), a.TargetRect.Height);
        Assert.Equal(workArea.Left, a.TargetRect.Left);
        Assert.Equal(workArea.Top, a.TargetRect.Top);

        var b = placements.First(p => p.SlotId == "B");
        Assert.Equal(workArea.Right, b.TargetRect.Right);
        Assert.Equal(workArea.Bottom, b.TargetRect.Bottom);

        var c = placements.First(p => p.SlotId == "C");
        Assert.Equal(workArea.Right, c.TargetRect.Right);
        Assert.Equal(workArea.Top, c.TargetRect.Top);

        var d = placements.First(p => p.SlotId == "D");
        Assert.Equal(workArea.Left, d.TargetRect.Left);
        Assert.Equal(workArea.Bottom, d.TargetRect.Bottom);
    }

    [Fact]
    public void ClassicPeekGrid_1920x1080_UsesNinetyPercent()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        var a = placements.First(p => p.SlotId == "A");
        Assert.Equal(1728, a.TargetRect.Width);
        Assert.Equal(972, a.TargetRect.Height);

        Assert.Equal(192, workArea.Right - a.TargetRect.Right);
        Assert.Equal(108, workArea.Bottom - a.TargetRect.Bottom);
    }

    [Fact]
    public void ClassicPeekGrid_UsesSmallRatio()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var settings = new AppSettings { WindowSizePreset = WindowSizePreset.Small };
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };
        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);
        var a = placements.First(p => p.SlotId == "A");

        Assert.Equal((int)Math.Round(workArea.Width * 0.90), a.TargetRect.Width);
        Assert.Equal((int)Math.Round(workArea.Height * 0.90), a.TargetRect.Height);
    }

    [Fact]
    public void ClassicPeekGrid_UsesMediumRatio()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var settings = new AppSettings { WindowSizePreset = WindowSizePreset.Medium };
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };
        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);
        var a = placements.First(p => p.SlotId == "A");

        Assert.Equal((int)Math.Round(workArea.Width * 0.95), a.TargetRect.Width);
        Assert.Equal((int)Math.Round(workArea.Height * 0.95), a.TargetRect.Height);
    }

    [Fact]
    public void ClassicPeekGrid_UsesLargeRatio()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var settings = new AppSettings { WindowSizePreset = WindowSizePreset.Large };
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };
        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);
        var a = placements.First(p => p.SlotId == "A");

        Assert.Equal((int)Math.Round(workArea.Width * 0.98), a.TargetRect.Width);
        Assert.Equal((int)Math.Round(workArea.Height * 0.98), a.TargetRect.Height);
    }

    [Fact]
    public void ClassicPeekGrid_SmallRatio_OffsetsCorrect()
    {
        var workArea = new Rect(0, 0, 1280, 720);
        var settings = new AppSettings { WindowSizePreset = WindowSizePreset.Small };
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea,
            LayoutEngine.GetPresetRatio(WindowSizePreset.Small));

        int spanW = (int)Math.Round(1280 * 0.90);
        int spanH = (int)Math.Round(720 * 0.90);
        int leftoverW = 1280 - spanW;
        int leftoverH = 720 - spanH;

        // TopLeft anchored to top-left
        Assert.Equal(0, slotRects["A"].Left);
        Assert.Equal(0, slotRects["A"].Top);
        Assert.Equal(spanW, slotRects["A"].Width);
        Assert.Equal(spanH, slotRects["A"].Height);

        // BottomRight anchored to bottom-right (offset = leftover)
        Assert.Equal(leftoverW, slotRects["B"].Left);
        Assert.Equal(leftoverH, slotRects["B"].Top);
        Assert.Equal(workArea.Right, slotRects["B"].Right);
        Assert.Equal(workArea.Bottom, slotRects["B"].Bottom);

        // TopCenter: centred horizontally, flush top
        int centerX = workArea.Left + (leftoverW / 2);
        Assert.Equal(centerX, slotRects["E"].Left);
        Assert.Equal(0, slotRects["E"].Top);
        Assert.Equal(spanW, slotRects["E"].Width);
        Assert.Equal(spanH, slotRects["E"].Height);

        // MiddleRight: centred vertically, flush right
        int centerY = workArea.Top + (leftoverH / 2);
        Assert.Equal(workArea.Right, slotRects["G"].Right);
        Assert.Equal(centerY, slotRects["G"].Top);
        Assert.Equal(spanW, slotRects["G"].Width);
        Assert.Equal(spanH, slotRects["G"].Height);
    }

    [Fact]
    public void ClassicPeekGrid_MediumRatio_OffsetsCorrect()
    {
        var workArea = new Rect(0, 0, 1280, 720);
        var settings = new AppSettings { WindowSizePreset = WindowSizePreset.Medium };
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea,
            LayoutEngine.GetPresetRatio(WindowSizePreset.Medium));

        int spanW = (int)Math.Round(1280 * 0.95);
        int spanH = (int)Math.Round(720 * 0.95);
        int leftoverW = 1280 - spanW;
        int leftoverH = 720 - spanH;

        Assert.Equal(0, slotRects["A"].Left);
        Assert.Equal(0, slotRects["A"].Top);
        Assert.Equal(spanW, slotRects["A"].Width);
        Assert.Equal(spanH, slotRects["A"].Height);

        Assert.Equal(leftoverW, slotRects["B"].Left);
        Assert.Equal(leftoverH, slotRects["B"].Top);
        Assert.Equal(workArea.Right, slotRects["B"].Right);
        Assert.Equal(workArea.Bottom, slotRects["B"].Bottom);

        int centerX = workArea.Left + (leftoverW / 2);
        Assert.Equal(centerX, slotRects["E"].Left);
        Assert.Equal(0, slotRects["E"].Top);

        int centerY = workArea.Top + (leftoverH / 2);
        Assert.Equal(workArea.Right, slotRects["G"].Right);
        Assert.Equal(centerY, slotRects["G"].Top);
    }

    [Fact]
    public void ClassicPeekGrid_LargeRatio_OffsetsCorrect()
    {
        var workArea = new Rect(0, 0, 1280, 720);
        var settings = new AppSettings { WindowSizePreset = WindowSizePreset.Large };
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea,
            LayoutEngine.GetPresetRatio(WindowSizePreset.Large));

        int spanW = (int)Math.Round(1280 * 0.98);
        int spanH = (int)Math.Round(720 * 0.98);
        int leftoverW = 1280 - spanW;
        int leftoverH = 720 - spanH;

        Assert.Equal(0, slotRects["A"].Left);
        Assert.Equal(0, slotRects["A"].Top);
        Assert.Equal(spanW, slotRects["A"].Width);
        Assert.Equal(spanH, slotRects["A"].Height);

        Assert.Equal(leftoverW, slotRects["B"].Left);
        Assert.Equal(leftoverH, slotRects["B"].Top);
        Assert.Equal(workArea.Right, slotRects["B"].Right);
        Assert.Equal(workArea.Bottom, slotRects["B"].Bottom);

        int centerX = workArea.Left + (leftoverW / 2);
        Assert.Equal(centerX, slotRects["E"].Left);
        Assert.Equal(0, slotRects["E"].Top);

        int centerY = workArea.Top + (leftoverH / 2);
        Assert.Equal(workArea.Right, slotRects["G"].Right);
        Assert.Equal(centerY, slotRects["G"].Top);
    }

    [Fact]
    public void ClassicPeekGrid_TwoWindows_A_and_B()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.Equal(2, placements.Count);
        Assert.Equal("A", placements[0].SlotId);
        Assert.Equal("B", placements[1].SlotId);
        Assert.DoesNotContain(placements, p => p.SlotId == "C");
        Assert.DoesNotContain(placements, p => p.SlotId == "D");
    }

    [Fact]
    public void ClassicPeekGrid_ThreeWindows_A_B_C()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.Equal(3, placements.Count);
        Assert.Equal("A", placements[0].SlotId);
        Assert.Equal("B", placements[1].SlotId);
        Assert.Equal("C", placements[2].SlotId);
        Assert.DoesNotContain(placements, p => p.SlotId == "D");
    }

    [Fact]
    public void ClassicPeekGrid_FourWindows_A_B_C_D()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.Equal(4, placements.Count);
        var slotIds = placements.Select(p => p.SlotId).ToList();
        Assert.Equal(new[] { "A", "B", "C", "D" }, slotIds);
    }

    [Fact]
    public void ClassicPeekGrid_FiveWindows_ManagesFiveSlots_AtoE()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var now = DateTime.Now;
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, IsPinned = true, FirstSeenAt = now },
            new() { Hwnd = (IntPtr)2, IsForeground = true, FirstSeenAt = now, LastFocusedAt = now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = now },
            new() { Hwnd = (IntPtr)5, FirstSeenAt = now.AddHours(-1) }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        // The layout now supports up to 8 windows per monitor, so the oldest window
        // (hwnd=5) is no longer dropped — it takes the fifth slot E.
        Assert.Equal(5, placements.Count);
        var slotIds = placements.Select(p => p.SlotId).ToList();
        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, slotIds);
        Assert.Contains(placements, p => p.Hwnd == (IntPtr)5 && p.SlotId == "E");
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void ClassicPeekGrid_EightWindows_FillsSlotsAtoH_InOrder(Rect workArea)
    {
        var settings = new AppSettings();
        var now = DateTime.Now;
        var windows = new List<ManagedWindow>();
        for (int i = 1; i <= 8; i++)
            windows.Add(new ManagedWindow { Hwnd = (IntPtr)i, FirstSeenAt = now.AddMinutes(-i) });

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.Equal(8, placements.Count);
        var slotIds = placements.Select(p => p.SlotId).ToList();
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F", "G", "H" }, slotIds);
        // Slot ids are unique — every window lands in its own slot.
        Assert.Equal(slotIds.Distinct().Count(), slotIds.Count);
    }

    [Fact]
    public void ClassicPeekGrid_MoreThanEightWindows_LimitsToEight()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var now = DateTime.Now;
        var windows = new List<ManagedWindow>();
        for (int i = 1; i <= 10; i++)
            windows.Add(new ManagedWindow { Hwnd = (IntPtr)i, FirstSeenAt = now.AddMinutes(-i) });

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        // Overflow keeps the existing behaviour shape: only the top-priority windows are
        // managed, the rest are ignored. The cap is now 8 instead of 4.
        Assert.Equal(8, placements.Count);
        Assert.DoesNotContain(placements, p => p.Hwnd == (IntPtr)9);
        Assert.DoesNotContain(placements, p => p.Hwnd == (IntPtr)10);
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void ClassicPeekGrid_SlotE_IsTopCentered(Rect workArea)
    {
        // 5th window → slot E (top centre): ~5% margin left/right, flush with the top edge.
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea);
        var e = slotRects["E"];
        int spanW = (int)Math.Round(workArea.Width * 0.90);
        int spanH = (int)Math.Round(workArea.Height * 0.90);

        Assert.Equal(workArea.Top, e.Top);
        Assert.Equal(spanW, e.Width);
        Assert.Equal(spanH, e.Height);
        // Centred horizontally: equal ~5% margins on both sides. Integer rounding can split
        // an odd leftover pixel unevenly, so allow a 1px asymmetry.
        int leftMargin = e.Left - workArea.Left;
        int rightMargin = workArea.Right - e.Right;
        Assert.True(Math.Abs(leftMargin - rightMargin) <= 1, $"Slot E not centred: left={leftMargin}, right={rightMargin}");
        Assert.True(e.Right <= workArea.Right, "Slot E right exceeds work area");
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void ClassicPeekGrid_SlotF_IsBottomCentered(Rect workArea)
    {
        // 6th window → slot F (bottom centre): ~5% margin left/right, flush with the bottom edge.
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea);
        var f = slotRects["F"];
        int spanW = (int)Math.Round(workArea.Width * 0.90);
        int spanH = (int)Math.Round(workArea.Height * 0.90);

        Assert.Equal(workArea.Bottom, f.Bottom);
        Assert.Equal(spanW, f.Width);
        Assert.Equal(spanH, f.Height);
        int leftMargin = f.Left - workArea.Left;
        int rightMargin = workArea.Right - f.Right;
        Assert.True(Math.Abs(leftMargin - rightMargin) <= 1, $"Slot F not centred: left={leftMargin}, right={rightMargin}");
        Assert.True(f.Top >= workArea.Top, "Slot F top above work area");
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void ClassicPeekGrid_SlotG_IsRightCentered(Rect workArea)
    {
        // 7th window → slot G (right centre): flush with the right edge, ~5% margin top/bottom.
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea);
        var g = slotRects["G"];
        int spanW = (int)Math.Round(workArea.Width * 0.90);
        int spanH = (int)Math.Round(workArea.Height * 0.90);

        Assert.Equal(workArea.Right, g.Right);
        Assert.Equal(spanW, g.Width);
        Assert.Equal(spanH, g.Height);
        int topMargin = g.Top - workArea.Top;
        int bottomMargin = workArea.Bottom - g.Bottom;
        Assert.True(Math.Abs(topMargin - bottomMargin) <= 1, $"Slot G not centred: top={topMargin}, bottom={bottomMargin}");
        Assert.True(g.Left >= workArea.Left, "Slot G left before work area");
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void ClassicPeekGrid_SlotH_IsLeftCentered(Rect workArea)
    {
        // 8th window → slot H (left centre): flush with the left edge, ~5% margin top/bottom.
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea);
        var h = slotRects["H"];
        int spanW = (int)Math.Round(workArea.Width * 0.90);
        int spanH = (int)Math.Round(workArea.Height * 0.90);

        Assert.Equal(workArea.Left, h.Left);
        Assert.Equal(spanW, h.Width);
        Assert.Equal(spanH, h.Height);
        int topMargin = h.Top - workArea.Top;
        int bottomMargin = workArea.Bottom - h.Bottom;
        Assert.True(Math.Abs(topMargin - bottomMargin) <= 1, $"Slot H not centred: top={topMargin}, bottom={bottomMargin}");
        Assert.True(h.Right <= workArea.Right, "Slot H right exceeds work area");
    }

    [Fact]
    public void ClassicPeekGrid_SlotsEtoH_KeepCornerMargins_Unchanged()
    {
        // The four corner slots (A–D) must keep their exact pre-existing anchors and the
        // single 10% strip they leave exposed — the new slots must not alter them.
        var workArea = new Rect(0, 0, 1280, 720);
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea);

        var a = slotRects["A"];
        Assert.Equal(0, a.Left);
        Assert.Equal(0, a.Top);
        Assert.Equal(1152, a.Width);
        Assert.Equal(648, a.Height);

        var b = slotRects["B"];
        Assert.Equal(128, b.Left);
        Assert.Equal(72, b.Top);
        Assert.Equal(1152, b.Width);
        Assert.Equal(648, b.Height);

        var c = slotRects["C"];
        Assert.Equal(128, c.Left);
        Assert.Equal(0, c.Top);
        Assert.Equal(1152, c.Width);
        Assert.Equal(648, c.Height);

        var d = slotRects["D"];
        Assert.Equal(0, d.Left);
        Assert.Equal(72, d.Top);
        Assert.Equal(1152, d.Width);
        Assert.Equal(648, d.Height);
    }

    [Fact]
    public void CalculateClassicPeekGridSlotRects_ReturnsEightSlots()
    {
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(new Rect(0, 0, 1920, 1080));

        Assert.Equal(8, slotRects.Count);
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F", "G", "H" }, slotRects.Keys.OrderBy(k => k).ToArray());
    }

    [Theory]
    [MemberData(nameof(GetResolutions))]
    public void ClassicPeekGrid_AllEightSlotsStayInsideWorkArea(Rect workArea)
    {
        var slotRects = _engine.CalculateClassicPeekGridSlotRects(workArea);

        foreach (var (slotId, rect) in slotRects)
        {
            Assert.True(rect.Left >= workArea.Left, $"Slot {slotId} left out of bounds");
            Assert.True(rect.Top >= workArea.Top, $"Slot {slotId} top out of bounds");
            Assert.True(rect.Right <= workArea.Right, $"Slot {slotId} right out of bounds");
            Assert.True(rect.Bottom <= workArea.Bottom, $"Slot {slotId} bottom out of bounds");
        }
    }

    [Fact]
    public void ClassicPeekGrid_OneWindow_TakesSlotA()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.Single(placements);
        Assert.Equal("A", placements[0].SlotId);
    }

    [Fact]
    public void ClassicPeekGrid_DoesNotPlaceSecondariesInsideFocusOnly()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        foreach (var sec in placements.Where(p => p.SlotId != "A"))
        {
            bool entirelyInsideA =
                sec.TargetRect.Left >= placements[0].TargetRect.Left &&
                sec.TargetRect.Top >= placements[0].TargetRect.Top &&
                sec.TargetRect.Right <= placements[0].TargetRect.Right &&
                sec.TargetRect.Bottom <= placements[0].TargetRect.Bottom;

            Assert.False(entirelyInsideA, $"Secondary {sec.SlotId} is entirely contained inside slot A — would be hidden");
        }
    }

    [Fact]
    public void ClassicPeekGrid_SecondariesHaveVisibleAreaOutsideA()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);
        var aRect = placements.First(p => p.SlotId == "A").TargetRect;

        foreach (var sec in placements.Where(p => p.SlotId != "A"))
        {
            int outsideLeft = Math.Max(0, aRect.Left - sec.TargetRect.Left);
            int outsideTop = Math.Max(0, aRect.Top - sec.TargetRect.Top);
            int outsideRight = Math.Max(0, sec.TargetRect.Right - aRect.Right);
            int outsideBottom = Math.Max(0, sec.TargetRect.Bottom - aRect.Bottom);

            bool hasVisibleOutside = outsideLeft > 0 || outsideTop > 0 || outsideRight > 0 || outsideBottom > 0;
            Assert.True(hasVisibleOutside, $"Secondary {sec.SlotId} has no visible area outside slot A");
        }
    }

    [Fact]
    public void ClassicPeekGrid_WindowsRemainOnScreen()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)3, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)4, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        foreach (var p in placements)
        {
            Assert.True(p.TargetRect.Left >= workArea.Left, $"{p.SlotId} left out of bounds");
            Assert.True(p.TargetRect.Top >= workArea.Top, $"{p.SlotId} top out of bounds");
            Assert.True(p.TargetRect.Right <= workArea.Right, $"{p.SlotId} right out of bounds");
            Assert.True(p.TargetRect.Bottom <= workArea.Bottom, $"{p.SlotId} bottom out of bounds");
        }
    }

    [Fact]
    public void ClassicPeekGrid_SecondariesAreNotAlmostFullyOffscreen()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now },
            new() { Hwnd = (IntPtr)2, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        foreach (var p in placements)
        {
            int visibleLeft = Math.Max(p.TargetRect.Left, workArea.Left);
            int visibleTop = Math.Max(p.TargetRect.Top, workArea.Top);
            int visibleRight = Math.Min(p.TargetRect.Right, workArea.Right);
            int visibleBottom = Math.Min(p.TargetRect.Bottom, workArea.Bottom);
            int visibleW = Math.Max(0, visibleRight - visibleLeft);
            int visibleH = Math.Max(0, visibleBottom - visibleTop);

            Assert.True(visibleW > 150, $"Window {p.SlotId} only {visibleW}px visible horizontally");
            Assert.True(visibleH > 120, $"Window {p.SlotId} only {visibleH}px visible vertically");
        }
    }
}
