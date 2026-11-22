using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class DirectionalFocusLayoutSnapshotServiceTests
{
    private static readonly Rect WorkArea = new(0, 0, 1920, 1080);

    private static Rect ExpectedSlotRect(string slotId)
        => new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea)[slotId];

    private static ManagedWindow Window(IntPtr hwnd, Rect rect, string title = "App")
        => new()
        {
            Hwnd = hwnd,
            Title = title,
            ClassName = "Class",
            ProcessName = "app.exe",
            CurrentRect = rect,
            IsVisible = true
        };

    private sealed class SingleMonitorResolver : IMonitorResolver
    {
        private readonly Rect _workArea;
        public SingleMonitorResolver(Rect workArea) { _workArea = workArea; }
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd) => new() { WorkArea = _workArea, IsPrimary = true };
        public MonitorInfo GetPrimaryMonitor() => new() { WorkArea = _workArea, IsPrimary = true };
    }

    private sealed class TwoMonitorResolver(MonitorInfo left, MonitorInfo right) : IMonitorResolver
    {
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd) => hwnd == (IntPtr)8 ? right : left;
        public MonitorInfo GetPrimaryMonitor() => left;
    }

    [Theory]
    [InlineData("Halves", false, 2)]
    [InlineData("Thirds", false, 3)]
    [InlineData("Custom", false, 2)]
    [InlineData("Custom", true, 3)]
    public void RebuildUsesTheRespectivePlacementRects(string mode, bool split, int expectedCount)
    {
        var engine = new LayoutEngine();
        var settings = new AppSettings();
        var zones = new CustomZoneLayout(SplitSecondary: split);
        var windows = Enumerable.Range(1, 3).Select(id => Window((IntPtr)id, default)).ToArray();
        var rectangles = engine.CalculatePlacements(mode, windows, WorkArea, settings, zones)
            .ToDictionary(placement => placement.Hwnd, placement => placement.TargetRect);
        var snapshot = new DirectionalFocusLayoutSnapshotService(
            new SingleMonitorResolver(WorkArea), hwnd => rectangles[hwnd],
            _ => true, _ => engine.GetSlotRects(mode, WorkArea, settings, zones));

        var slotMap = snapshot.BuildSlotMap(WorkArea, windows.Select(window => window.Hwnd));

        Assert.Equal(expectedCount, slotMap.Count);
        Assert.Equal((IntPtr)1, slotMap[DirectionalFocusSlot.MiddleLeft]);
        Assert.All(slotMap, entry => Assert.True(snapshot.IsWindowStillInSlot(entry.Value, WorkArea, entry.Key)));
    }

    [Fact]
    public void RebuildUsesPerMonitorLayoutAndTheTopEligibleWindowInEachStack()
    {
        var left = new MonitorInfo { ProfileId = "left", WorkArea = WorkArea with { Left = -1920, Top = -60 } };
        var right = new MonitorInfo { ProfileId = "right", WorkArea = WorkArea };
        var resolver = new TwoMonitorResolver(left, right);
        var settings = new AppSettings
        {
            LayoutMode = "Thirds",
            MonitorProfiles = new() { ["left"] = new("Halves", []) }
        };
        var engine = new LayoutEngine();
        IReadOnlyDictionary<string, Rect> GetSlots(MonitorInfo monitor)
        {
            var profile = MonitorLayoutSettings.Resolve(settings, monitor);
            return engine.GetSlotRects(profile.Mode, monitor.WorkArea, settings, profile.Zones);
        }

        var leftRect = GetSlots(left)["Left"];
        var rects = new Dictionary<IntPtr, Rect>
        {
            [(IntPtr)1] = leftRect, // another desktop
            [(IntPtr)2] = leftRect, // minimized
            [(IntPtr)3] = leftRect with { Left = leftRect.Left + 300 }, // dragged away
            [(IntPtr)4] = leftRect, // first eligible window in this slot
            [(IntPtr)5] = leftRect,
            [(IntPtr)8] = GetSlots(right)["Col2"]
        };
        var snapshot = new DirectionalFocusLayoutSnapshotService(resolver, hwnd => rects[hwnd],
            hwnd => hwnd != (IntPtr)1 && hwnd != (IntPtr)2, GetSlots);

        var leftMap = snapshot.BuildSlotMap(left.WorkArea, rects.Keys);
        var rightMap = snapshot.BuildSlotMap(right.WorkArea, rects.Keys);

        Assert.Equal((IntPtr)4, Assert.Single(leftMap).Value);
        Assert.Equal((IntPtr)4, leftMap[DirectionalFocusSlot.MiddleLeft]);
        Assert.Equal((IntPtr)8, Assert.Single(rightMap).Value);
        Assert.Equal((IntPtr)8, rightMap[DirectionalFocusSlot.TopCenter]);
        Assert.False(snapshot.IsWindowStillInSlot((IntPtr)8, left.WorkArea, DirectionalFocusSlot.TopCenter));
    }

    [Fact]
    public void CloseGridRectsKeepTheirExactSlotAndTheStackHead()
    {
        var engine = new LayoutEngine();
        var grid = engine.CalculateClassicPeekGridSlotRects(WorkArea, LayoutEngine.GetPresetRatio(WindowSizePreset.Large));
        var snapshot = new DirectionalFocusLayoutSnapshotService(new SingleMonitorResolver(WorkArea),
            hwnd => hwnd == (IntPtr)3 ? grid["F"] : grid["B"], _ => true, _ => grid);

        var map = snapshot.BuildSlotMap(WorkArea, [(IntPtr)2, (IntPtr)1, (IntPtr)3]);

        Assert.Equal(2, map.Count);
        Assert.Equal((IntPtr)2, map[DirectionalFocusSlot.BottomRight]);
        Assert.Equal((IntPtr)3, map[DirectionalFocusSlot.BottomCenter]);
    }

    [Fact]
    public void SmallCustomZoneRemainsFocusableAfterArrangeShrinksTheWindow()
    {
        var area = WorkArea with { Height = 800 };
        var settings = new AppSettings();
        var slots = new LayoutEngine().GetSlotRects("Custom", area, settings, new(60, true, 80));
        var raw = new RawWindowInfo
        {
            Hwnd = (IntPtr)3,
            CurrentRect = slots["Custom3"],
            IsVisible = true,
            ProcessName = "editor.exe",
            ProcessId = Environment.ProcessId + 1,
            Title = "Editor"
        };
        var classifier = new WindowClassifier(settings, _ => true);
        var snapshot = new DirectionalFocusLayoutSnapshotService(new SingleMonitorResolver(area),
            _ => raw.CurrentRect, _ => classifier.IsEligibleForDirectionalFocus(raw), _ => slots);

        Assert.False(classifier.IsEligible(raw));
        Assert.Equal(raw.Hwnd, snapshot.BuildSlotMap(area, [raw.Hwnd])[DirectionalFocusSlot.BottomRight]);
    }

    private static DirectionalFocusLayoutSnapshotService CreateService(
        Func<IntPtr, Rect> rectFor,
        Func<IntPtr, bool>? isOnCurrentDesktop = null)
    {
        return new DirectionalFocusLayoutSnapshotService(
            new SingleMonitorResolver(WorkArea),
            new LayoutEngine(),
            rectFor,
            isOnCurrentDesktop ?? (_ => true));
    }

    [Fact]
    public void MatchesTopLeftSlot()
    {
        var rectA = ExpectedSlotRect("A");
        var service = CreateService(hwnd => hwnd == (IntPtr)10 ? rectA : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)10 });

        Assert.Equal((IntPtr)10, map[DirectionalFocusSlot.TopLeft]);
    }

    [Fact]
    public void MatchesBottomRightSlot()
    {
        var rectB = ExpectedSlotRect("B");
        var service = CreateService(hwnd => hwnd == (IntPtr)11 ? rectB : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)11 });

        Assert.Equal((IntPtr)11, map[DirectionalFocusSlot.BottomRight]);
    }

    [Fact]
    public void MatchesTopRightSlot()
    {
        var rectC = ExpectedSlotRect("C");
        var service = CreateService(hwnd => hwnd == (IntPtr)12 ? rectC : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)12 });

        Assert.Equal((IntPtr)12, map[DirectionalFocusSlot.TopRight]);
    }

    [Fact]
    public void MatchesBottomLeftSlot()
    {
        var rectD = ExpectedSlotRect("D");
        var service = CreateService(hwnd => hwnd == (IntPtr)13 ? rectD : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)13 });

        Assert.Equal((IntPtr)13, map[DirectionalFocusSlot.BottomLeft]);
    }

    [Fact]
    public void MatchesTopCenterSlot_E()
    {
        var rectE = ExpectedSlotRect("E");
        var service = CreateService(hwnd => hwnd == (IntPtr)14 ? rectE : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)14 });

        Assert.Equal((IntPtr)14, map[DirectionalFocusSlot.TopCenter]);
    }

    [Fact]
    public void MatchesBottomCenterSlot_F()
    {
        var rectF = ExpectedSlotRect("F");
        var service = CreateService(hwnd => hwnd == (IntPtr)15 ? rectF : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)15 });

        Assert.Equal((IntPtr)15, map[DirectionalFocusSlot.BottomCenter]);
    }

    [Fact]
    public void MatchesMiddleRightSlot_G()
    {
        var rectG = ExpectedSlotRect("G");
        var service = CreateService(hwnd => hwnd == (IntPtr)16 ? rectG : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)16 });

        Assert.Equal((IntPtr)16, map[DirectionalFocusSlot.MiddleRight]);
    }

    [Fact]
    public void MatchesMiddleLeftSlot_H()
    {
        var rectH = ExpectedSlotRect("H");
        var service = CreateService(hwnd => hwnd == (IntPtr)17 ? rectH : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)17 });

        Assert.Equal((IntPtr)17, map[DirectionalFocusSlot.MiddleLeft]);
    }

    [Fact]
    public void ReturnsEmpty_WhenRectsDoNotMatchPeekDowsLayout()
    {
        // Window sitting at an arbitrary non-layout position.
        var service = CreateService(_ => new Rect(317, 244, 800, 600));

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)20 });

        Assert.Empty(map);
    }

    [Fact]
    public void UsesTolerance_MatchesWithin40Pixels()
    {
        var rectA = ExpectedSlotRect("A");
        // Perturb by ±25px on every edge — must still match.
        var drifted = new Rect(
            rectA.Left + 25,
            rectA.Top - 18,
            rectA.Width + 12,
            rectA.Height - 30);

        var service = CreateService(hwnd => hwnd == (IntPtr)30 ? drifted : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)30 });

        Assert.True(map.ContainsKey(DirectionalFocusSlot.TopLeft));
        Assert.Equal((IntPtr)30, map[DirectionalFocusSlot.TopLeft]);
    }

    [Fact]
    public void UsesTolerance_RejectsBeyondTolerance()
    {
        var rectA = ExpectedSlotRect("A");
        // Shrink the size by 500px — far beyond the 40px tolerance. (Drifting only the
        // Left/Top would land on another overlapping slot, since the four ClassicPeekGrid
        // slots share the same Width/Height and overlap by 90%.)
        var drifted = new Rect(rectA.Left, rectA.Top, rectA.Width - 500, rectA.Height - 500);

        var service = CreateService(hwnd => hwnd == (IntPtr)31 ? drifted : default);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)31 });

        Assert.Empty(map);
    }

    [Fact]
    public void DoesNotMapWindowFromOtherMonitor()
    {
        var otherWorkArea = new Rect(1920, 0, 1920, 1080);
        var rectAOnOther = new LayoutEngine().CalculateClassicPeekGridSlotRects(otherWorkArea)["A"];

        var rectFor = (IntPtr hwnd) => hwnd == (IntPtr)40 ? rectAOnOther : default;
        var monitorFor = new SingleMonitorResolver(otherWorkArea);

        var service = new DirectionalFocusLayoutSnapshotService(
            monitorFor,
            new LayoutEngine(),
            rectFor,
            _ => true);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)40 });

        Assert.Empty(map);
    }

    [Fact]
    public void HandlesTwoWindowLayout_AAndBOnly()
    {
        var slotRects = new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea);
        var rectFor = (IntPtr hwnd) => hwnd switch
        {
            IntPtr h when h == (IntPtr)50 => slotRects["A"],
            IntPtr h when h == (IntPtr)51 => slotRects["B"],
            _ => default
        };

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)50, (IntPtr)51 });

        Assert.Equal((IntPtr)50, map[DirectionalFocusSlot.TopLeft]);
        Assert.Equal((IntPtr)51, map[DirectionalFocusSlot.BottomRight]);
        Assert.False(map.ContainsKey(DirectionalFocusSlot.TopRight));
        Assert.False(map.ContainsKey(DirectionalFocusSlot.BottomLeft));
    }

    [Fact]
    public void HandlesFourWindowLayout_AllSlots()
    {
        var slotRects = new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea);
        var rectFor = (IntPtr hwnd) => hwnd switch
        {
            IntPtr h when h == (IntPtr)60 => slotRects["A"],
            IntPtr h when h == (IntPtr)61 => slotRects["B"],
            IntPtr h when h == (IntPtr)62 => slotRects["C"],
            IntPtr h when h == (IntPtr)63 => slotRects["D"],
            _ => default
        };

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)60, (IntPtr)61, (IntPtr)62, (IntPtr)63 });

        Assert.Equal((IntPtr)60, map[DirectionalFocusSlot.TopLeft]);
        Assert.Equal((IntPtr)61, map[DirectionalFocusSlot.BottomRight]);
        Assert.Equal((IntPtr)62, map[DirectionalFocusSlot.TopRight]);
        Assert.Equal((IntPtr)63, map[DirectionalFocusSlot.BottomLeft]);
    }

    [Fact]
    public void HandlesThreeWindowLayout_A_B_C_Only()
    {
        var slotRects = new LayoutEngine().CalculateClassicPeekGridSlotRects(WorkArea);
        var rectFor = (IntPtr hwnd) => hwnd switch
        {
            IntPtr h when h == (IntPtr)70 => slotRects["A"],
            IntPtr h when h == (IntPtr)71 => slotRects["B"],
            IntPtr h when h == (IntPtr)72 => slotRects["C"],
            _ => default
        };

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)70, (IntPtr)71, (IntPtr)72 });

        Assert.Equal((IntPtr)70, map[DirectionalFocusSlot.TopLeft]);
        Assert.Equal((IntPtr)71, map[DirectionalFocusSlot.BottomRight]);
        Assert.Equal((IntPtr)72, map[DirectionalFocusSlot.TopRight]);
        Assert.False(map.ContainsKey(DirectionalFocusSlot.BottomLeft));
    }

    [Fact]
    public void DoesNotMapWindowNotOnCurrentVirtualDesktop()
    {
        var rectA = ExpectedSlotRect("A");
        var service = CreateService(hwnd => hwnd == (IntPtr)80 ? rectA : default, hwnd => hwnd != (IntPtr)80);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)80 });

        Assert.Empty(map);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var service = CreateService(_ => default);
        var map = service.BuildSlotMap(WorkArea, Array.Empty<IntPtr>());
        Assert.Empty(map);
    }

    [Fact]
    public void FirstMatchingWindowWins_WhenTwoWindowsOverlapSameSlot()
    {
        var rectA = ExpectedSlotRect("A");
        var rectFor = (IntPtr hwnd) => hwnd == (IntPtr)90 || hwnd == (IntPtr)91 ? rectA : default;

        var service = CreateService(rectFor);

        var map = service.BuildSlotMap(WorkArea, new[] { (IntPtr)90, (IntPtr)91 });

        Assert.Equal((IntPtr)90, map[DirectionalFocusSlot.TopLeft]);
    }
}
