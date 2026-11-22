using System;
using System.Collections.Generic;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Reconstructs directional targets from the current visible window positions and
/// the effective layout of the monitor, without moving any windows.
/// </summary>
public sealed class DirectionalFocusLayoutSnapshotService
{
    public const int DefaultTolerancePx = 40;

    private readonly IMonitorResolver _monitorResolver;
    private readonly Func<IntPtr, Rect> _getWindowRect;
    private readonly Func<IntPtr, bool> _isWindowEligible;
    private readonly Func<MonitorInfo, IReadOnlyDictionary<string, Rect>> _getSlotRects;
    private readonly int _tolerancePx;
    private readonly FileLogger? _logger;

    public DirectionalFocusLayoutSnapshotService(
        IMonitorResolver monitorResolver,
        Func<IntPtr, Rect> getWindowRect,
        Func<IntPtr, bool> isWindowEligible,
        Func<MonitorInfo, IReadOnlyDictionary<string, Rect>> getSlotRects,
        int tolerancePx = DefaultTolerancePx,
        FileLogger? logger = null)
    {
        _monitorResolver = monitorResolver;
        _getWindowRect = getWindowRect;
        _isWindowEligible = isWindowEligible;
        _getSlotRects = getSlotRects;
        _tolerancePx = tolerancePx;
        _logger = logger;
    }

    public DirectionalFocusLayoutSnapshotService(
        IMonitorResolver monitorResolver,
        LayoutEngine layoutEngine,
        Func<IntPtr, Rect> getWindowRect,
        Func<IntPtr, bool> isOnCurrentVirtualDesktop,
        Func<double> getRatio,
        int tolerancePx = DefaultTolerancePx,
        FileLogger? logger = null)
        : this(monitorResolver, getWindowRect, isOnCurrentVirtualDesktop,
               monitor => layoutEngine.CalculateClassicPeekGridSlotRects(monitor.WorkArea, getRatio()),
               tolerancePx, logger)
    {
    }

    public DirectionalFocusLayoutSnapshotService(
        IMonitorResolver monitorResolver,
        LayoutEngine layoutEngine,
        Func<IntPtr, Rect> getWindowRect,
        Func<IntPtr, bool> isOnCurrentVirtualDesktop,
        int tolerancePx = DefaultTolerancePx,
        FileLogger? logger = null)
        : this(monitorResolver, layoutEngine, getWindowRect, isOnCurrentVirtualDesktop,
               () => LayoutEngine.GetPresetRatio(WindowSizePreset.Small), tolerancePx, logger)
    {
    }

    /// <summary>
    /// Candidates are in top-to-bottom Z order. The first eligible window to
    /// match each zone wins. For a tween, the zone from its accepted plan
    /// substitutes for its transient rectangle.
    /// </summary>
    public IReadOnlyDictionary<DirectionalFocusSlot, IntPtr> BuildSlotMap(
        Rect monitorWorkArea,
        IEnumerable<IntPtr> candidateHwnds,
        IReadOnlyDictionary<IntPtr, WindowPlacement>? animatingSlots = null)
    {
        var result = new Dictionary<DirectionalFocusSlot, IntPtr>();
        IReadOnlyDictionary<string, Rect>? slotRects = null;

        foreach (var hwnd in candidateHwnds)
        {
            var monitor = GetEligibleMonitor(hwnd, monitorWorkArea);
            if (monitor is null) continue;

            slotRects ??= _getSlotRects(monitor);
            var slot = animatingSlots is not null && animatingSlots.TryGetValue(hwnd, out var placement)
                ? GetPlannedSlot(placement, slotRects)
                : FindMatchingSlot(_getWindowRect(hwnd), slotRects);

            if (slot is { } matched && result.TryAdd(matched, hwnd))
                _logger?.Info($"Directional focus snapshot matched: hwnd={hwnd}, slot={matched}, monitor={monitorWorkArea}");
        }

        return result;
    }

    public bool IsWindowStillInSlot(IntPtr hwnd, Rect monitorWorkArea, DirectionalFocusSlot slot)
    {
        var monitor = GetEligibleMonitor(hwnd, monitorWorkArea);
        return monitor is not null && FindMatchingSlot(_getWindowRect(hwnd), _getSlotRects(monitor)) == slot;
    }

    public bool IsPlannedSlotStillValid(WindowPlacement placement, Rect monitorWorkArea, DirectionalFocusSlot slot)
    {
        var monitor = GetEligibleMonitor(placement.Hwnd, monitorWorkArea);
        return monitor is not null && GetPlannedSlot(placement, _getSlotRects(monitor)) == slot;
    }

    public bool IsThirdsCenterColumn(IntPtr hwnd, Rect monitorWorkArea)
    {
        var monitor = GetEligibleMonitor(hwnd, monitorWorkArea);
        return monitor is not null && _getSlotRects(monitor).ContainsKey("Col2");
    }

    private MonitorInfo? GetEligibleMonitor(IntPtr hwnd, Rect monitorWorkArea)
    {
        if (hwnd == IntPtr.Zero || !_isWindowEligible(hwnd))
            return null;

        var monitor = _monitorResolver.GetMonitorForWindow(hwnd);
        return !monitor.IsFallback && monitor.WorkArea == monitorWorkArea ? monitor : null;
    }

    private static DirectionalFocusSlot? GetPlannedSlot(
        WindowPlacement placement, IReadOnlyDictionary<string, Rect> slotRects)
        => slotRects.TryGetValue(placement.SlotId, out var expected) && expected == placement.TargetRect
            ? DirectionalFocusRegistry.MapSlotId(placement.SlotId, slotRects.ContainsKey("Custom3"))
            : null;

    private DirectionalFocusSlot? FindMatchingSlot(Rect rect, IReadOnlyDictionary<string, Rect> slotRects)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        DirectionalFocusSlot? bestMatch = null;
        int bestDistance = int.MaxValue;
        bool customSplit = slotRects.ContainsKey("Custom3");

        foreach (var (slotId, expected) in slotRects)
        {
            var slot = DirectionalFocusRegistry.MapSlotId(slotId, customSplit);
            if (slot is null) continue;

            int left = Math.Abs(rect.Left - expected.Left);
            int top = Math.Abs(rect.Top - expected.Top);
            int width = Math.Abs(rect.Width - expected.Width);
            int height = Math.Abs(rect.Height - expected.Height);
            if (left > _tolerancePx || top > _tolerancePx || width > _tolerancePx || height > _tolerancePx)
                continue;

            // Grid positions can be closer than the tolerance at larger presets.
            // Identify the nearest zone before considering which window heads its stack.
            int distance = left + top + width + height;
            if (distance >= bestDistance) continue;

            bestMatch = slot;
            bestDistance = distance;
        }

        return bestMatch;
    }
}
