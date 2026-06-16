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
    public void CalculatePlacements_1Window_FullWorkArea_ByDefault()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1920, 1080);
        var windows = new List<ManagedWindow> { new() { Hwnd = (IntPtr)1, IsVisible = true, FirstSeenAt = DateTime.Now } };

        var placements = _engine.CalculatePlacements(windows, workArea, settings);

        Assert.Single(placements);
        Assert.Equal("FocusLarge", placements[0].SlotId);
        Assert.Equal(workArea, placements[0].TargetRect);
    }

    [Fact]
    public void CalculatePlacements_1Window_TopLeftSlot_WhenConfigured()
    {
        var settings = new AppSettings { SingleWindowMode = "TopLeftSlot" };
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
    public void CalculatePlacements_ExactRects_1920x1080()
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

        var slotA = placements.First(p => p.SlotId == "A");
        Assert.Equal(0, slotA.TargetRect.Left);
        Assert.Equal(0, slotA.TargetRect.Top);
        Assert.Equal(1440, slotA.TargetRect.Width);
        Assert.Equal(720, slotA.TargetRect.Height);

        var slotB = placements.First(p => p.SlotId == "B");
        Assert.Equal(480, slotB.TargetRect.Left);
        Assert.Equal(360, slotB.TargetRect.Top);
        Assert.Equal(1440, slotB.TargetRect.Width);
        Assert.Equal(720, slotB.TargetRect.Height);

        var slotC = placements.First(p => p.SlotId == "C");
        Assert.Equal(480, slotC.TargetRect.Left);
        Assert.Equal(0, slotC.TargetRect.Top);
        Assert.Equal(1440, slotC.TargetRect.Width);
        Assert.Equal(720, slotC.TargetRect.Height);

        var slotD = placements.First(p => p.SlotId == "D");
        Assert.Equal(0, slotD.TargetRect.Left);
        Assert.Equal(360, slotD.TargetRect.Top);
        Assert.Equal(1440, slotD.TargetRect.Width);
        Assert.Equal(720, slotD.TargetRect.Height);
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
        Assert.Equal(1040, a.TargetRect.Width);
        Assert.Equal(520, a.TargetRect.Height);

        var b = placements.First(p => p.SlotId == "B");
        Assert.Equal(240, b.TargetRect.Left);
        Assert.Equal(200, b.TargetRect.Top);
        Assert.Equal(1040, b.TargetRect.Width);
        Assert.Equal(520, b.TargetRect.Height);

        var c = placements.First(p => p.SlotId == "C");
        Assert.Equal(240, c.TargetRect.Left);
        Assert.Equal(0, c.TargetRect.Top);
        Assert.Equal(1040, c.TargetRect.Width);
        Assert.Equal(520, c.TargetRect.Height);

        var d = placements.First(p => p.SlotId == "D");
        Assert.Equal(0, d.TargetRect.Left);
        Assert.Equal(200, d.TargetRect.Top);
        Assert.Equal(1040, d.TargetRect.Width);
        Assert.Equal(520, d.TargetRect.Height);
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
    public void ClassicPeekGrid_OneWindow_FullWorkArea()
    {
        var settings = new AppSettings();
        var workArea = new Rect(0, 0, 1280, 720);
        var windows = new List<ManagedWindow>
        {
            new() { Hwnd = (IntPtr)1, FirstSeenAt = DateTime.Now }
        };

        var placements = _engine.CalculateClassicPeekGridPlacements(windows, workArea, settings);

        Assert.Single(placements);
        Assert.Equal("FocusLarge", placements[0].SlotId);
        Assert.Equal(workArea, placements[0].TargetRect);
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
    }

    [Fact]
    public void ClassicPeekGrid_FourWindows_AllSlots()
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
        Assert.Contains("A", slotIds);
        Assert.Contains("B", slotIds);
        Assert.Contains("C", slotIds);
        Assert.Contains("D", slotIds);
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
