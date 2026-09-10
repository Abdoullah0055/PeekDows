using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public sealed class MultiMonitorLayoutService
{
    private readonly IMonitorResolver _monitorResolver;
    private readonly LayoutEngine _layoutEngine;
    private readonly LoneWindowMaximizePolicy? _loneWindowPolicy;
    private readonly FileLogger? _logger;

    public MultiMonitorLayoutService(IMonitorResolver monitorResolver, LayoutEngine layoutEngine, FileLogger? logger = null)
        : this(monitorResolver, layoutEngine, null, logger)
    {
    }

    public MultiMonitorLayoutService(
        IMonitorResolver monitorResolver,
        LayoutEngine layoutEngine,
        LoneWindowMaximizePolicy? loneWindowPolicy,
        FileLogger? logger = null)
    {
        _monitorResolver = monitorResolver;
        _layoutEngine = layoutEngine;
        _loneWindowPolicy = loneWindowPolicy;
        _logger = logger;
    }

    public IReadOnlyList<WindowPlacement> CalculatePlacementsByMonitor(
        IReadOnlyList<ManagedWindow> windows,
        AppSettings settings)
    {
        _logger?.Info("Multi-monitor arrange started");
        _logger?.Info($"Arrangeable windows total={windows.Count}");

        if (windows.Count == 0)
        {
            _logger?.Info("No arrangeable windows, returning empty placements");
            return [];
        }

        var windowsByMonitor = GroupWindowsByMonitor(windows);

        _logger?.Info($"Connected monitors count={windowsByMonitor.Count}");

        if (_loneWindowPolicy is not null)
        {
            // B2: prefer handle-keyed state so a 1px WorkArea change doesn't orphan the flag.
            var byHandle = new Dictionary<IntPtr, (Rect workArea, int count)>();
            foreach (var group in windowsByMonitor)
                byHandle[group.Key.Handle] = (group.Key.WorkArea, group.Value.Count);
            // Use handle-aware overload when any real handle present; otherwise fall back to rect.
            if (byHandle.Keys.Any(h => h != IntPtr.Zero))
                _loneWindowPolicy.OnArrangeStarting(byHandle);
            else
            {
                var counts = new Dictionary<Rect, int>();
                foreach (var group in windowsByMonitor)
                    counts[group.Key.WorkArea] = group.Value.Count;
                _loneWindowPolicy.OnArrangeStarting(counts);
            }
        }

        var allPlacements = new List<WindowPlacement>();

        foreach (var group in windowsByMonitor)
        {
            var monitorInfo = group.Key;
            var monitorWindows = group.Value;

            if (monitorWindows.Count == 0)
            {
                _logger?.Info($"Skipping monitor workArea={monitorInfo.WorkArea} because no arrangeable windows");
                continue;
            }

            _logger?.Info($"Monitor group created: workArea={monitorInfo.WorkArea}, windowCount={monitorWindows.Count}, isPrimary={monitorInfo.IsPrimary}");

            // Lone-window rule (per monitor): exactly one arrangeable window → native
            // maximize, unless the user manually restored a PeekDows-maximized window on
            // this monitor — then the window is left completely alone this arrange.
            // Without a wired policy, fall through to the legacy slot layout (warn loudly).
            if (monitorWindows.Count == 1 && _loneWindowPolicy is null)
            {
                // A missing policy silently reverts to legacy slot placement, which is
                // exactly the failure mode where lone windows stop maximizing unnoticed.
                _logger?.Warn($"Lone window on monitor workArea={monitorInfo.WorkArea} but no LoneWindowMaximizePolicy wired; legacy layout used");
            }

            if (monitorWindows.Count == 1 && _loneWindowPolicy is not null)
            {
                var only = monitorWindows[0];
                if (_loneWindowPolicy.ShouldMaximizeLoneWindow(monitorInfo.Handle, monitorInfo.WorkArea))
                {
                    _logger?.Info($"Lone window on monitor workArea={monitorInfo.WorkArea}: Maximize placement (hwnd={only.Hwnd})");
                    allPlacements.Add(new WindowPlacement
                    {
                        Hwnd = only.Hwnd,
                        SlotId = "Maximize",
                        TargetRect = monitorInfo.WorkArea,
                        Kind = PlacementKind.Maximize
                    });
                    continue;
                }

                _logger?.Info($"Lone window on monitor workArea={monitorInfo.WorkArea} left as-is (user restored)");
                continue;
            }

            _logger?.Info($"Calculating placements for monitor workArea={monitorInfo.WorkArea}, windowCount={monitorWindows.Count}");

            var placements = _layoutEngine.CalculateClassicPeekGridPlacements(monitorWindows, monitorInfo.WorkArea, settings);

            // A window that is currently maximized (and was allowed through the controller's
            // maximized filter) must be restored before gliding into its slot.
            var maximizedHwnds = monitorWindows.Where(w => w.IsMaximized).Select(w => w.Hwnd).ToHashSet();
            foreach (var placement in placements)
            {
                if (maximizedHwnds.Contains(placement.Hwnd))
                {
                    allPlacements.Add(new WindowPlacement
                    {
                        Hwnd = placement.Hwnd,
                        SlotId = placement.SlotId,
                        TargetRect = placement.TargetRect,
                        Activate = placement.Activate,
                        PreserveZOrder = placement.PreserveZOrder,
                        BringToFront = placement.BringToFront,
                        Kind = PlacementKind.RestoreAndReposition
                    });
                }
                else
                {
                    allPlacements.Add(placement);
                }
            }

            _logger?.Info($"Placements for monitor workArea={monitorInfo.WorkArea}, placementCount={placements.Count}");
        }

        _logger?.Info($"Combined placement count={allPlacements.Count}");
        _logger?.Info("Multi-monitor arrange completed");

        return allPlacements;
    }

    internal Dictionary<MonitorInfo, List<ManagedWindow>> GroupWindowsByMonitor(IReadOnlyList<ManagedWindow> windows)
    {
        var groups = new Dictionary<MonitorInfo, List<ManagedWindow>>(new MonitorInfoEqualityComparer());

        foreach (var window in windows)
        {
            MonitorInfo monitor;

            try
            {
                monitor = _monitorResolver.GetMonitorForWindow(window.Hwnd);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Monitor detection failed for hwnd={window.Hwnd}, using primary monitor: {ex.Message}");
                monitor = _monitorResolver.GetPrimaryMonitor();
            }

            _logger?.Info($"Window assigned to monitor: hwnd={window.Hwnd}, title={window.Title}, workArea={monitor.WorkArea}, isPrimary={monitor.IsPrimary}");

            // Never compute placements against a synthetic fallback monitor. If the real
            // monitor could not be resolved (GetMonitorInfo failed), the resolver returns a
            // fallback 1920x1080 marked IsFallback; placing windows into it would produce
            // bogus placements on an invented monitor, so skip the window entirely.
            if (monitor.IsFallback)
            {
                _logger?.Warn($"Window on fallback monitor skipped (monitor resolve failed): hwnd={window.Hwnd}, title={window.Title}, no placement computed");
                continue;
            }

            if (!groups.TryGetValue(monitor, out var list))
            {
                list = new List<ManagedWindow>();
                groups[monitor] = list;
            }

            list.Add(window);
        }

        if (groups.Count == 0)
        {
            var primary = _monitorResolver.GetPrimaryMonitor();
            groups[primary] = new List<ManagedWindow>();
        }

        return groups;
    }

    private sealed class MonitorInfoEqualityComparer : IEqualityComparer<MonitorInfo>
    {
        public bool Equals(MonitorInfo? x, MonitorInfo? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;

            if (x.Handle != IntPtr.Zero && y.Handle != IntPtr.Zero)
                return x.Handle == y.Handle;

            return x.WorkArea == y.WorkArea;
        }

        public int GetHashCode(MonitorInfo obj)
        {
            if (obj.Handle != IntPtr.Zero)
                return obj.Handle.GetHashCode();

            return obj.WorkArea.GetHashCode();
        }
    }
}
