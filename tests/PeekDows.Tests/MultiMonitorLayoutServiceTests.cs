using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class MultiMonitorLayoutServiceTests
{
    private sealed class FakeMonitorResolver : IMonitorResolver
    {
        private readonly Dictionary<IntPtr, MonitorInfo> _map;
        private readonly MonitorInfo _primary;

        public FakeMonitorResolver(Dictionary<IntPtr, MonitorInfo> map, MonitorInfo primary)
        {
            _map = map;
            _primary = primary;
        }

        public MonitorInfo GetMonitorForWindow(IntPtr hwnd)
        {
            if (_map.TryGetValue(hwnd, out var info))
                return info;

            throw new InvalidOperationException($"No monitor mapping for hwnd={hwnd}");
        }

        public MonitorInfo GetPrimaryMonitor() => _primary;
    }

    private static ManagedWindow MakeWindow(int hwndId, string title = "")
    {
        return new ManagedWindow
        {
            Hwnd = (IntPtr)hwndId,
            Title = title,
            FirstSeenAt = DateTime.Now
        };
    }

    [Fact]
    public void MultiMonitorLayout_SingleMonitor_BehavesLikeClassicPeekGrid()
    {
        var workArea = new Rect(0, 0, 1280, 720);
        var laptop = new MonitorInfo { Handle = (IntPtr)1, WorkArea = workArea, FullArea = workArea, IsPrimary = true };

        var resolver = new FakeMonitorResolver(
            new Dictionary<IntPtr, MonitorInfo>
            {
                [(IntPtr)10] = laptop,
                [(IntPtr)20] = laptop,
                [(IntPtr)30] = laptop,
                [(IntPtr)40] = laptop
            },
            laptop);

        var engine = new LayoutEngine();
        var service = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();

        var windows = new List<ManagedWindow>
        {
            MakeWindow(10), MakeWindow(20), MakeWindow(30), MakeWindow(40)
        };

        var placements = service.CalculatePlacementsByMonitor(windows, settings);

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
    public void MultiMonitorLayout_TwoOnLaptop_ThreeOnExternal_ArrangesIndependently()
    {
        var laptopWorkArea = new Rect(0, 0, 1280, 720);
        var externalWorkArea = new Rect(1280, 0, 1920, 1080);

        var laptop = new MonitorInfo { Handle = (IntPtr)1, WorkArea = laptopWorkArea, FullArea = laptopWorkArea, IsPrimary = true };
        var external = new MonitorInfo { Handle = (IntPtr)2, WorkArea = externalWorkArea, FullArea = externalWorkArea, IsPrimary = false };

        var resolver = new FakeMonitorResolver(
            new Dictionary<IntPtr, MonitorInfo>
            {
                [(IntPtr)1] = laptop,
                [(IntPtr)2] = laptop,
                [(IntPtr)3] = external,
                [(IntPtr)4] = external,
                [(IntPtr)5] = external
            },
            laptop);

        var engine = new LayoutEngine();
        var service = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();

        var windows = new List<ManagedWindow>
        {
            MakeWindow(1), MakeWindow(2), MakeWindow(3), MakeWindow(4), MakeWindow(5)
        };

        var placements = service.CalculatePlacementsByMonitor(windows, settings);

        Assert.Equal(5, placements.Count);

        var laptopPlacements = placements.Where(p => p.TargetRect.Left >= laptopWorkArea.Left && p.TargetRect.Right <= laptopWorkArea.Right).ToList();
        var externalPlacements = placements.Where(p => p.TargetRect.Left >= externalWorkArea.Left && p.TargetRect.Right <= externalWorkArea.Right).ToList();

        Assert.Equal(2, laptopPlacements.Count);
        Assert.Equal(3, externalPlacements.Count);

        var laptopHwnds = laptopPlacements.Select(p => p.Hwnd).ToHashSet();
        var externalHwnds = externalPlacements.Select(p => p.Hwnd).ToHashSet();
        Assert.Equal(new HashSet<IntPtr> { (IntPtr)1, (IntPtr)2 }, laptopHwnds);
        Assert.Equal(new HashSet<IntPtr> { (IntPtr)3, (IntPtr)4, (IntPtr)5 }, externalHwnds);

        Assert.Contains(laptopPlacements, p => p.SlotId == "A");
        Assert.Contains(laptopPlacements, p => p.SlotId == "B");

        Assert.Contains(externalPlacements, p => p.SlotId == "A");
        Assert.Contains(externalPlacements, p => p.SlotId == "B");
        Assert.Contains(externalPlacements, p => p.SlotId == "C");

        var laptopA = laptopPlacements.First(p => p.SlotId == "A");
        Assert.Equal(0, laptopA.TargetRect.Left);
        Assert.Equal(0, laptopA.TargetRect.Top);
        Assert.Equal(1152, laptopA.TargetRect.Width);
        Assert.Equal(648, laptopA.TargetRect.Height);

        var laptopB = laptopPlacements.First(p => p.SlotId == "B");
        Assert.Equal(128, laptopB.TargetRect.Left);
        Assert.Equal(72, laptopB.TargetRect.Top);
        Assert.Equal(1152, laptopB.TargetRect.Width);
        Assert.Equal(648, laptopB.TargetRect.Height);

        var externalA = externalPlacements.First(p => p.SlotId == "A");
        Assert.Equal(1280, externalA.TargetRect.Left);
        Assert.Equal(0, externalA.TargetRect.Top);
        Assert.Equal(1728, externalA.TargetRect.Width);
        Assert.Equal(972, externalA.TargetRect.Height);

        var externalB = externalPlacements.First(p => p.SlotId == "B");
        Assert.Equal(1472, externalB.TargetRect.Left);
        Assert.Equal(108, externalB.TargetRect.Top);
        Assert.Equal(1728, externalB.TargetRect.Width);
        Assert.Equal(972, externalB.TargetRect.Height);

        var externalC = externalPlacements.First(p => p.SlotId == "C");
        Assert.Equal(1472, externalC.TargetRect.Left);
        Assert.Equal(0, externalC.TargetRect.Top);
        Assert.Equal(1728, externalC.TargetRect.Width);
        Assert.Equal(972, externalC.TargetRect.Height);
    }

    [Fact]
    public void MultiMonitorLayout_LimitsPerMonitor_NotGlobally()
    {
        // Documents that the per-monitor cap applies independently per screen, not as a
        // global budget. With the eight-slot layout, monitor 1 (5 windows) keeps all five
        // and monitor 2 (2 windows) keeps both — neither is capped by the other's count.
        var workArea1 = new Rect(0, 0, 1920, 1080);
        var workArea2 = new Rect(1920, 0, 1920, 1080);

        var monitor1 = new MonitorInfo { Handle = (IntPtr)1, WorkArea = workArea1, FullArea = workArea1, IsPrimary = true };
        var monitor2 = new MonitorInfo { Handle = (IntPtr)2, WorkArea = workArea2, FullArea = workArea2, IsPrimary = false };

        var map = new Dictionary<IntPtr, MonitorInfo>();
        for (int i = 1; i <= 5; i++) map[(IntPtr)i] = monitor1;
        for (int i = 6; i <= 7; i++) map[(IntPtr)i] = monitor2;

        var resolver = new FakeMonitorResolver(map, monitor1);

        var engine = new LayoutEngine();
        var service = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();

        var now = DateTime.Now;
        var windows = new List<ManagedWindow>();
        for (int i = 1; i <= 5; i++)
            windows.Add(new ManagedWindow { Hwnd = (IntPtr)i, FirstSeenAt = now, IsPinned = i == 1 });
        for (int i = 6; i <= 7; i++)
            windows.Add(new ManagedWindow { Hwnd = (IntPtr)i, FirstSeenAt = now });

        var placements = service.CalculatePlacementsByMonitor(windows, settings);

        Assert.Equal(7, placements.Count);

        var monitor1Placements = placements.Where(p =>
            p.TargetRect.Left >= workArea1.Left && p.TargetRect.Right <= workArea1.Right).ToList();
        var monitor2Placements = placements.Where(p =>
            p.TargetRect.Left >= workArea2.Left && p.TargetRect.Right <= workArea2.Right).ToList();

        Assert.Equal(5, monitor1Placements.Count);
        Assert.Equal(2, monitor2Placements.Count);
    }

    [Fact]
    public void MultiMonitorLayout_EightPerMonitor_ArrangesIndependently()
    {
        // Each monitor must be able to fill all eight slots independently of the other.
        var workArea1 = new Rect(0, 0, 1920, 1080);
        var workArea2 = new Rect(1920, 0, 1920, 1080);

        var monitor1 = new MonitorInfo { Handle = (IntPtr)1, WorkArea = workArea1, FullArea = workArea1, IsPrimary = true };
        var monitor2 = new MonitorInfo { Handle = (IntPtr)2, WorkArea = workArea2, FullArea = workArea2, IsPrimary = false };

        var map = new Dictionary<IntPtr, MonitorInfo>();
        // Windows 1–8 on monitor 1, windows 9–16 on monitor 2.
        for (int i = 1; i <= 8; i++) map[(IntPtr)i] = monitor1;
        for (int i = 9; i <= 16; i++) map[(IntPtr)i] = monitor2;

        var resolver = new FakeMonitorResolver(map, monitor1);
        var engine = new LayoutEngine();
        var service = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();

        var now = DateTime.Now;
        var windows = new List<ManagedWindow>();
        for (int i = 1; i <= 16; i++)
            windows.Add(new ManagedWindow { Hwnd = (IntPtr)i, FirstSeenAt = now.AddMinutes(-i) });

        var placements = service.CalculatePlacementsByMonitor(windows, settings);

        // 8 per monitor, 16 in total — the cap is per-monitor, not global.
        Assert.Equal(16, placements.Count);

        var monitor1Placements = placements.Where(p =>
            p.TargetRect.Left >= workArea1.Left && p.TargetRect.Right <= workArea1.Right).ToList();
        var monitor2Placements = placements.Where(p =>
            p.TargetRect.Left >= workArea2.Left && p.TargetRect.Right <= workArea2.Right).ToList();

        Assert.Equal(8, monitor1Placements.Count);
        Assert.Equal(8, monitor2Placements.Count);

        // Both monitors fill the full A–H slot sequence.
        var monitor1Slots = monitor1Placements.Select(p => p.SlotId).OrderBy(s => s).ToArray();
        var monitor2Slots = monitor2Placements.Select(p => p.SlotId).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F", "G", "H" }, monitor1Slots);
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F", "G", "H" }, monitor2Slots);

        // No window strays onto the other monitor.
        Assert.All(monitor1Placements, p => Assert.True(p.TargetRect.Right <= workArea1.Right));
        Assert.All(monitor2Placements, p => Assert.True(p.TargetRect.Left >= workArea2.Left));
    }

    [Fact]
    public void MultiMonitorLayout_TargetRectsStayInsideOriginalMonitorWorkArea()
    {
        var monitor1WorkArea = new Rect(0, 0, 1280, 720);
        var monitor2WorkArea = new Rect(-1920, 0, 1920, 1080);

        var monitor1 = new MonitorInfo { Handle = (IntPtr)1, WorkArea = monitor1WorkArea, FullArea = monitor1WorkArea, IsPrimary = true };
        var monitor2 = new MonitorInfo { Handle = (IntPtr)2, WorkArea = monitor2WorkArea, FullArea = monitor2WorkArea, IsPrimary = false };

        var resolver = new FakeMonitorResolver(
            new Dictionary<IntPtr, MonitorInfo>
            {
                [(IntPtr)10] = monitor1,
                [(IntPtr)20] = monitor1,
                [(IntPtr)30] = monitor2,
                [(IntPtr)40] = monitor2
            },
            monitor1);

        var engine = new LayoutEngine();
        var service = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();

        var windows = new List<ManagedWindow>
        {
            MakeWindow(10), MakeWindow(20), MakeWindow(30), MakeWindow(40)
        };

        var placements = service.CalculatePlacementsByMonitor(windows, settings);

        Assert.Equal(4, placements.Count);

        foreach (var p in placements.Where(p => p.Hwnd == (IntPtr)10 || p.Hwnd == (IntPtr)20))
        {
            Assert.True(p.TargetRect.Left >= monitor1WorkArea.Left, $"hwnd={p.Hwnd} left {p.TargetRect.Left} < monitor1 min {monitor1WorkArea.Left}");
            Assert.True(p.TargetRect.Top >= monitor1WorkArea.Top, $"hwnd={p.Hwnd} top {p.TargetRect.Top} < monitor1 min {monitor1WorkArea.Top}");
            Assert.True(p.TargetRect.Right <= monitor1WorkArea.Right, $"hwnd={p.Hwnd} right {p.TargetRect.Right} > monitor1 max {monitor1WorkArea.Right}");
            Assert.True(p.TargetRect.Bottom <= monitor1WorkArea.Bottom, $"hwnd={p.Hwnd} bottom {p.TargetRect.Bottom} > monitor1 max {monitor1WorkArea.Bottom}");
        }

        foreach (var p in placements.Where(p => p.Hwnd == (IntPtr)30 || p.Hwnd == (IntPtr)40))
        {
            Assert.True(p.TargetRect.Left >= monitor2WorkArea.Left, $"hwnd={p.Hwnd} left {p.TargetRect.Left} < monitor2 min {monitor2WorkArea.Left}");
            Assert.True(p.TargetRect.Top >= monitor2WorkArea.Top, $"hwnd={p.Hwnd} top {p.TargetRect.Top} < monitor2 min {monitor2WorkArea.Top}");
            Assert.True(p.TargetRect.Right <= monitor2WorkArea.Right, $"hwnd={p.Hwnd} right {p.TargetRect.Right} > monitor2 max {monitor2WorkArea.Right}");
            Assert.True(p.TargetRect.Bottom <= monitor2WorkArea.Bottom, $"hwnd={p.Hwnd} bottom {p.TargetRect.Bottom} > monitor2 max {monitor2WorkArea.Bottom}");
        }
    }

    [Fact]
    public void MultiMonitorLayout_EmptyMonitor_DoesNotCreatePlacements()
    {
        var workArea1 = new Rect(0, 0, 1920, 1080);
        var workArea2 = new Rect(1920, 0, 1920, 1080);
        var workArea3 = new Rect(3840, 0, 1920, 1080);

        var monitor1 = new MonitorInfo { Handle = (IntPtr)1, WorkArea = workArea1, FullArea = workArea1, IsPrimary = true };
        var monitor2 = new MonitorInfo { Handle = (IntPtr)2, WorkArea = workArea2, FullArea = workArea2, IsPrimary = false };
        var monitor3 = new MonitorInfo { Handle = (IntPtr)3, WorkArea = workArea3, FullArea = workArea3, IsPrimary = false };

        var resolver = new FakeMonitorResolver(
            new Dictionary<IntPtr, MonitorInfo>
            {
                [(IntPtr)1] = monitor1,
                [(IntPtr)2] = monitor1,
                [(IntPtr)5] = monitor3
            },
            monitor1);

        var engine = new LayoutEngine();
        var service = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();

        var windows = new List<ManagedWindow>
        {
            MakeWindow(1), MakeWindow(2), MakeWindow(5)
        };

        var placements = service.CalculatePlacementsByMonitor(windows, settings);

        Assert.Equal(3, placements.Count);

        Assert.All(placements, p =>
        {
            bool onMonitor1 = p.TargetRect.Left >= workArea1.Left && p.TargetRect.Right <= workArea1.Right;
            bool onMonitor3 = p.TargetRect.Left >= workArea3.Left && p.TargetRect.Right <= workArea3.Right;
            Assert.True(onMonitor1 || onMonitor3, $"Placement for hwnd={p.Hwnd} is not on monitor1 or monitor3");
        });
    }

    [Fact]
    public void MultiMonitorLayout_MonitorDetectionFailure_FallsBackToPrimary()
    {
        var primaryWorkArea = new Rect(0, 0, 1920, 1080);
        var primary = new MonitorInfo { Handle = (IntPtr)1, WorkArea = primaryWorkArea, FullArea = primaryWorkArea, IsPrimary = true };

        var resolver = new ThrowingFakeMonitorResolver(
            new Dictionary<IntPtr, MonitorInfo>
            {
                [(IntPtr)1] = primary
            },
            [(IntPtr)99],
            primary);

        var engine = new LayoutEngine();
        var service = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();

        var windows = new List<ManagedWindow>
        {
            MakeWindow(1),
            MakeWindow(99)
        };

        var placements = service.CalculatePlacementsByMonitor(windows, settings);

        Assert.Equal(2, placements.Count);

        Assert.All(placements, p =>
        {
            Assert.True(p.TargetRect.Left >= primaryWorkArea.Left, $"hwnd={p.Hwnd} left={p.TargetRect.Left} is outside primary");
            Assert.True(p.TargetRect.Right <= primaryWorkArea.Right, $"hwnd={p.Hwnd} right={p.TargetRect.Right} is outside primary");
        });
    }

    private sealed class ThrowingFakeMonitorResolver : IMonitorResolver
    {
        private readonly Dictionary<IntPtr, MonitorInfo> _map;
        private readonly HashSet<IntPtr> _throwFor;
        private readonly MonitorInfo _primary;

        public ThrowingFakeMonitorResolver(Dictionary<IntPtr, MonitorInfo> map, IEnumerable<IntPtr> throwFor, MonitorInfo primary)
        {
            _map = map;
            _throwFor = new HashSet<IntPtr>(throwFor);
            _primary = primary;
        }

        public MonitorInfo GetMonitorForWindow(IntPtr hwnd)
        {
            if (_throwFor.Contains(hwnd))
                throw new InvalidOperationException($"Simulated monitor detection failure for hwnd={hwnd}");

            return _map[hwnd];
        }

        public MonitorInfo GetPrimaryMonitor() => _primary;
    }
}
