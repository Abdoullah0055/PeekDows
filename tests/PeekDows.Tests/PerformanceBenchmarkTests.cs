using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace PeekDows.Tests;

/// <summary>
/// Harness de benchmarks reproductibles pour mesurer l'impact des optimisations perf.
/// Chaque test chauffe, mesure temps + allocations, et log via ITestOutputHelper.
/// Pour rerun: dotnet test --filter "PerformanceBenchmark" -c Release --logger "console;verbosity=detailed"
/// Les résultats sont à reporter dans docs/audits/2026-09-10_*-benchmarks-performance-avant-apres.md
/// </summary>
[Trait("Category", "PerformanceBenchmark")]
public class PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarkTests(ITestOutputHelper output) => _output = output;

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    private static RawWindowInfo MakeRaw(int hwnd, string title = "App", int w = 800, int h = 600, string proc = "app.exe")
        => new()
        {
            Hwnd = (IntPtr)hwnd,
            Title = title,
            ClassName = "Chrome_WidgetWin_1",
            ProcessId = 1000 + hwnd,
            ProcessName = proc,
            CurrentRect = new Rect(0, 0, w, h),
            IsVisible = true,
            IsMinimized = false,
            IsMaximized = false,
            IsForeground = false,
            IsCloaked = false
        };

    private static ManagedWindow MakeManaged(int hwnd, Rect? rect = null)
        => new()
        {
            Hwnd = (IntPtr)hwnd,
            Title = $"Win{hwnd}",
            ClassName = "Chrome_WidgetWin_1",
            ProcessId = 1000 + hwnd,
            ProcessName = "app.exe",
            CurrentRect = rect ?? new Rect(0, 0, 800, 600),
            IsVisible = true,
            FirstSeenAt = DateTime.Now,
            LastSeenAt = DateTime.Now
        };

    private sealed class FakeResolver : IMonitorResolver
    {
        private readonly Rect[] _areas;
        public FakeResolver(params Rect[] areas) => _areas = areas;
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd)
        {
            // Distribue round-robin pour simuler 2 moniteurs
            int idx = (hwnd.ToInt32() & 1) == 0 ? 0 : (_areas.Length > 1 ? 1 : 0);
            return new MonitorInfo { Handle = (IntPtr)(100 + idx), WorkArea = _areas[idx], IsPrimary = idx == 0 };
        }
        public MonitorInfo GetPrimaryMonitor() => new() { Handle = (IntPtr)100, WorkArea = _areas[0], IsPrimary = true };
    }

    private void Bench(string name, Action action, int iterations = 200, int warmup = 20)
    {
        for (int i = 0; i < warmup; i++) action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action();
        sw.Stop();
        long endBytes = GC.GetAllocatedBytesForCurrentThread();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double avgMs = totalMs / iterations;
        double opsPerSec = 1000.0 / avgMs;
        long bytesPerOp = (endBytes - startBytes) / iterations;

        _output.WriteLine($"[BENCH] {name}: iterations={iterations} total={totalMs:F2}ms avg={avgMs:F4}ms/op ops/s={opsPerSec:F0} alloc={bytesPerOp}B/op");
    }

    private void BenchWithStats(string name, Func<double> singleRunMs, int iterations = 100)
    {
        // warmup
        for (int i = 0; i < 10; i++) singleRunMs();

        var samples = new List<double>(iterations);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) samples.Add(singleRunMs());
        long endBytes = GC.GetAllocatedBytesForCurrentThread();

        samples.Sort();
        double avg = samples.Average();
        double p50 = samples[iterations / 2];
        double p95 = samples[(int)(iterations * 0.95)];
        double p99 = samples[(int)(iterations * 0.99)];
        double min = samples.First();
        double max = samples.Last();
        long bytesPerOp = (endBytes - startBytes) / iterations;

        _output.WriteLine($"[BENCH] {name}: n={iterations} avg={avg:F4}ms p50={p50:F4}ms p95={p95:F4}ms p99={p99:F4}ms min={min:F4}ms max={max:F4}ms alloc={bytesPerOp}B/op");
    }

    // ------------------------------------------------------------------
    // 1) WindowDiscoveryService.Refresh
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_Discovery_Refresh_20Windows()
    {
        var raws = Enumerable.Range(1, 20).Select(i => MakeRaw(i)).ToList();
        var settings = new AppSettings();
        var classifier = new WindowClassifier(settings);
        var svc = new WindowDiscoveryService(() => raws);

        Bench("Discovery.Refresh 20 wins", () => { svc.Refresh(classifier); }, iterations: 300);
    }

    [Fact]
    public void Perf_Discovery_Refresh_50Windows()
    {
        var raws = Enumerable.Range(1, 50).Select(i => MakeRaw(i)).ToList();
        var settings = new AppSettings();
        var classifier = new WindowClassifier(settings);
        var svc = new WindowDiscoveryService(() => raws);

        Bench("Discovery.Refresh 50 wins", () => { svc.Refresh(classifier); }, iterations: 200);
    }

    [Fact]
    public void Perf_Discovery_Refresh_100Windows()
    {
        var raws = Enumerable.Range(1, 100).Select(i => MakeRaw(i)).ToList();
        var settings = new AppSettings();
        var classifier = new WindowClassifier(settings);
        var svc = new WindowDiscoveryService(() => raws);

        Bench("Discovery.Refresh 100 wins", () => { svc.Refresh(classifier); }, iterations: 100);
    }

    // ------------------------------------------------------------------
    // 2) WindowClassifier.IsEligible
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_Classifier_IsEligible_100Windows()
    {
        var raws = Enumerable.Range(1, 100).Select(i => MakeRaw(i)).ToList();
        var classifier = new WindowClassifier(new AppSettings());

        Bench("Classifier.IsEligible 100 wins", () =>
        {
            foreach (var r in raws) classifier.IsEligible(r);
        }, iterations: 500);
    }

    // ------------------------------------------------------------------
    // 3) LayoutEngine
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_Layout_CalculateClassicPeekGrid_4Windows()
    {
        var engine = new LayoutEngine();
        var workArea = new Rect(0, 0, 1920, 1080);
        var settings = new AppSettings();
        var wins = Enumerable.Range(1, 4).Select(i => MakeManaged(i)).ToList();

        Bench("Layout.ClassicPeekGrid 4 wins", () => { engine.CalculateClassicPeekGridPlacements(wins, workArea, settings); }, iterations: 1000);
    }

    [Fact]
    public void Perf_Layout_CalculateClassicPeekGrid_8Windows()
    {
        var engine = new LayoutEngine();
        var workArea = new Rect(0, 0, 1920, 1080);
        var settings = new AppSettings();
        var wins = Enumerable.Range(1, 8).Select(i => MakeManaged(i)).ToList();

        Bench("Layout.ClassicPeekGrid 8 wins", () => { engine.CalculateClassicPeekGridPlacements(wins, workArea, settings); }, iterations: 1000);
    }

    [Fact]
    public void Perf_Layout_CalculateSlotRects_10000Calls_NoCache()
    {
        var engine = new LayoutEngine();
        var workArea = new Rect(0, 0, 1920, 1080);
        double ratio = LayoutEngine.GetPresetRatio(WindowSizePreset.Small);

        Bench("Layout.SlotRects 1 call (alloc dict)", () => { engine.CalculateClassicPeekGridSlotRects(workArea, ratio); }, iterations: 5000);
    }

    // ------------------------------------------------------------------
    // 4) MultiMonitorLayoutService
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_MultiMonitor_GroupAndPlace_40Windows_2Monitors()
    {
        var workArea1 = new Rect(0, 0, 1920, 1080);
        var workArea2 = new Rect(1920, 0, 1920, 1080);
        var resolver = new FakeResolver(workArea1, workArea2);
        var engine = new LayoutEngine();
        var svc = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();
        var wins = Enumerable.Range(1, 40).Select(i => MakeManaged(i)).ToList();

        Bench("MultiMonitor.Group+Place 40 wins 2 mons", () => { svc.CalculatePlacementsByMonitor(wins, settings); }, iterations: 300);
    }

    [Fact]
    public void Perf_MultiMonitor_GroupOnly_40Windows()
    {
        var workArea1 = new Rect(0, 0, 1920, 1080);
        var workArea2 = new Rect(1920, 0, 1920, 1080);
        var resolver = new FakeResolver(workArea1, workArea2);
        var engine = new LayoutEngine();
        var svc = new MultiMonitorLayoutService(resolver, engine);
        var wins = Enumerable.Range(1, 40).Select(i => MakeManaged(i)).ToList();

        Bench("MultiMonitor.GroupOnly 40 wins", () => { svc.GroupWindowsByMonitor(wins); }, iterations: 500);
    }

    // ------------------------------------------------------------------
    // 5) DirectionalFocus snapshot
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_Snapshot_BuildSlotMap_8Windows()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var engine = new LayoutEngine();
        var slotRects = engine.CalculateClassicPeekGridSlotRects(workArea);
        var slotList = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
        var rectByHwnd = new Dictionary<IntPtr, Rect>();
        for (int i = 0; i < 8; i++) rectByHwnd[(IntPtr)(10 + i)] = slotRects[slotList[i]];

        var resolver = new FakeResolver(workArea);
        var svc = new DirectionalFocusLayoutSnapshotService(resolver, engine, hwnd => rectByHwnd.TryGetValue(hwnd, out var r) ? r : default, _ => true);
        var hwnds = Enumerable.Range(10, 8).Select(i => (IntPtr)i).ToList();

        Bench("Snapshot.BuildSlotMap 8 wins", () => { svc.BuildSlotMap(workArea, hwnds); }, iterations: 500);
    }

    [Fact]
    public void Perf_Snapshot_IsWindowStillInSlot_1Window()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var engine = new LayoutEngine();
        var rectA = engine.CalculateClassicPeekGridSlotRects(workArea)["A"];
        var resolver = new FakeResolver(workArea);
        var svc = new DirectionalFocusLayoutSnapshotService(resolver, engine, _ => rectA, _ => true);

        Bench("Snapshot.IsWindowStillInSlot 1 win", () => { svc.IsWindowStillInSlot((IntPtr)10, workArea, DirectionalFocusSlot.TopLeft); }, iterations: 2000);
    }

    // ------------------------------------------------------------------
    // 6) FileLogger (sync IO) — mesure le coût actuel P-A2
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_FileLogger_Write_200Lines()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"PeekDows_Perf_{Guid.NewGuid():N}");
        var logger = new FileLogger(tmp, maxLogFileSizeBytes: 10 * 1024 * 1024, maxLogBackups: 2);

        // Bench 200 lignes d'affilée (simule 3-4 arranges)
        BenchWithStats("FileLogger 200 lines (sync IO)", () =>
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) logger.Info($"perf line {i} hwnd={i} rect=0,0,800,600");
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }, iterations: 20);

        try { Directory.Delete(tmp, true); } catch { }
    }

    [Fact]
    public void Perf_FileLogger_Write_SingleLine()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"PeekDows_Perf_{Guid.NewGuid():N}");
        var logger = new FileLogger(tmp);

        Bench("FileLogger 1 line", () => logger.Info("single perf line"), iterations: 200);

        try { Directory.Delete(tmp, true); } catch { }
    }

    // ------------------------------------------------------------------
    // 7) WindowPlacementService + UnstableTracker
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_Placement_Apply_8Windows_FakePositioner()
    {
        var logger = new FileLogger(Path.Combine(Path.GetTempPath(), $"PeekDows_Perf_{Guid.NewGuid():N}"));
        var tracker = new UnstableWindowTracker();
        var fake = new FakeWindowPositioner();
        var svc = new WindowPlacementService(fake, _ => true, _ => false, logger, tracker, _ => true, null);
        var workArea = new Rect(0, 0, 1920, 1080);
        // 8 placements distincts
        var placements = Enumerable.Range(1, 8).Select(i => new WindowPlacement
        {
            Hwnd = (IntPtr)i,
            SlotId = ((char)('A' + i - 1)).ToString(),
            TargetRect = new Rect((i - 1) * 10, (i - 1) * 10, 800, 600),
            BringToFront = false
        }).ToList();

        Bench("Placement.Apply 8 wins (fake)", () => { svc.ApplyPlacements(placements); }, iterations: 300);

        try { Directory.Delete(Path.GetDirectoryName(logger.LogFilePath)!, true); } catch { }
    }

    // ------------------------------------------------------------------
    // 8) UnstableWindowTracker
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_UnstableTracker_IsUnstable_1000Checks()
    {
        var tracker = new UnstableWindowTracker();
        tracker.MarkUnstable((IntPtr)42, DateTime.Now, "test");
        var hwnds = Enumerable.Range(1, 100).Select(i => (IntPtr)i).ToList();

        Bench("Unstable.IsUnstable 100 checks", () =>
        {
            foreach (var h in hwnds) tracker.IsUnstable(h);
        }, iterations: 1000);
    }

    // ------------------------------------------------------------------
    // 9) End-to-end Arrange simulation (Discovery+Classifier+Layout+Placement) 40 wins
    // ------------------------------------------------------------------
    [Fact]
    public void Perf_E2E_Arrange_40Windows_FakeIO()
    {
        var workArea1 = new Rect(0, 0, 1920, 1080);
        var workArea2 = new Rect(1920, 0, 1920, 1080);
        var resolver = new FakeResolver(workArea1, workArea2);
        var engine = new LayoutEngine();
        var layoutSvc = new MultiMonitorLayoutService(resolver, engine);
        var settings = new AppSettings();
        var raws = Enumerable.Range(1, 40).Select(i => MakeRaw(i)).ToList();
        var classifier = new WindowClassifier(settings, _ => true);
        var discovery = new WindowDiscoveryService(() => raws);
        var tmp = Path.Combine(Path.GetTempPath(), $"PeekDows_Perf_{Guid.NewGuid():N}");
        var logger = new FileLogger(tmp);
        var tracker = new UnstableWindowTracker();
        var fakePos = new FakeWindowPositioner();
        var placementSvc = new WindowPlacementService(fakePos, _ => true, _ => false, logger, tracker, _ => true, null);

        BenchWithStats("E2E Arrange 40 wins (fake Win32)", () =>
        {
            var sw = Stopwatch.StartNew();
            var diff = discovery.Refresh(classifier);
            var placements = layoutSvc.CalculatePlacementsByMonitor(diff.Current, settings);
            placementSvc.ApplyPlacements(placements);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }, iterations: 50);

        try { Directory.Delete(tmp, true); } catch { }
    }
}
