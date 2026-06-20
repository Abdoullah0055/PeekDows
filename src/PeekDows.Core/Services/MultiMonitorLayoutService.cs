using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public sealed class MultiMonitorLayoutService
{
    private readonly IMonitorResolver _monitorResolver;
    private readonly LayoutEngine _layoutEngine;
    private readonly FileLogger? _logger;

    public MultiMonitorLayoutService(IMonitorResolver monitorResolver, LayoutEngine layoutEngine, FileLogger? logger = null)
    {
        _monitorResolver = monitorResolver;
        _layoutEngine = layoutEngine;
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
            _logger?.Info($"Calculating placements for monitor workArea={monitorInfo.WorkArea}, windowCount={monitorWindows.Count}");

            var placements = _layoutEngine.CalculateClassicPeekGridPlacements(monitorWindows, monitorInfo.WorkArea, settings);

            _logger?.Info($"Placements for monitor workArea={monitorInfo.WorkArea}, placementCount={placements.Count}");

            allPlacements.AddRange(placements);
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
