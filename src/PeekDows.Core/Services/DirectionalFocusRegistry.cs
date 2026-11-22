using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public sealed class DirectionalFocusRegistry
{
    private static readonly IReadOnlyDictionary<IntPtr, WindowPlacement> NoPlannedSlots =
        new Dictionary<IntPtr, WindowPlacement>();
    private readonly Dictionary<Rect, Dictionary<DirectionalFocusSlot, IntPtr>> _slotsByMonitor = new();
    private readonly Dictionary<Rect, Dictionary<IntPtr, WindowPlacement>> _plannedSlotsByMonitor = new();
    private readonly FileLogger? _logger;
    private readonly Func<IntPtr, bool> _isWindowValid;
    private readonly Func<IntPtr, bool> _isOnCurrentVirtualDesktop;

    public DirectionalFocusRegistry(FileLogger? logger = null)
        : this(hwnd => NativeMethods.IsWindow(hwnd), hwnd => true, logger) { }

    public DirectionalFocusRegistry(
        Func<IntPtr, bool> isWindowValid,
        FileLogger? logger = null)
        : this(isWindowValid, hwnd => true, logger) { }

    public DirectionalFocusRegistry(
        Func<IntPtr, bool> isWindowValid,
        Func<IntPtr, bool> isOnCurrentVirtualDesktop,
        FileLogger? logger = null)
    {
        _isWindowValid = isWindowValid;
        _isOnCurrentVirtualDesktop = isOnCurrentVirtualDesktop;
        _logger = logger;
    }

    public void Clear()
    {
        _slotsByMonitor.Clear();
        _plannedSlotsByMonitor.Clear();
    }

    public IReadOnlyDictionary<IntPtr, WindowPlacement> GetPlannedSlotsForMonitor(Rect monitorWorkArea)
        => _plannedSlotsByMonitor.TryGetValue(monitorWorkArea, out var plans) ? plans : NoPlannedSlots;

    public void UpdateFromPlacements(
        IReadOnlyList<WindowPlacement> placements,
        IMonitorResolver monitorResolver,
        IReadOnlyList<IntPtr>? topToBottom = null,
        Func<MonitorInfo, IReadOnlyDictionary<string, Rect>>? getSlotRects = null)
    {
        Clear();
        if (topToBottom is null)
        {
            ApplyPlacements(placements, monitorResolver, getSlotRects);
            return;
        }

        var ranks = topToBottom.Distinct().Select((hwnd, rank) => (hwnd, rank))
            .ToDictionary(entry => entry.hwnd, entry => entry.rank);
        ApplyPlacements(placements.OrderBy(p => ranks.GetValueOrDefault(p.Hwnd, int.MaxValue)), monitorResolver, getSlotRects);
    }

    /// <summary>
    /// Replaces the slot→hwnd map for a single monitor without touching other
    /// monitors. Used by the on-demand layout snapshot rebuild so it can refresh
    /// only the monitor under the cursor (or every monitor) without clearing
    /// the whole registry.
    /// </summary>
    public void SetSlotsForMonitor(Rect monitorWorkArea, IReadOnlyDictionary<DirectionalFocusSlot, IntPtr> slots)
    {
        if (slots == null || slots.Count == 0)
        {
            _slotsByMonitor.Remove(monitorWorkArea);
            _logger?.Info($"DirectionalFocusRegistry: cleared slots for monitor={monitorWorkArea}");
            return;
        }

        var slotMap = new Dictionary<DirectionalFocusSlot, IntPtr>(slots);
        _slotsByMonitor[monitorWorkArea] = slotMap;
        _logger?.Info($"DirectionalFocusRegistry: snapshot-set slots for monitor={monitorWorkArea}, count={slotMap.Count}");
    }

    private void ApplyPlacements(
        IEnumerable<WindowPlacement> placements,
        IMonitorResolver monitorResolver,
        Func<MonitorInfo, IReadOnlyDictionary<string, Rect>>? getSlotRects)
    {
        var slotsByMonitor = new Dictionary<Rect, IReadOnlyDictionary<string, Rect>>();
        foreach (var placement in placements)
        {
            if (!_isWindowValid(placement.Hwnd) || !_isOnCurrentVirtualDesktop(placement.Hwnd))
                continue;

            var monitor = monitorResolver.GetMonitorForWindow(placement.Hwnd);

            // Never key a window into a fake monitor bucket. When the real monitor could not
            // be resolved (GetMonitorInfo failed), the resolver returns a synthetic
            // 1920x1080 fallback marked IsFallback. Mapping into it would collapse unrelated
            // windows onto one fake monitor and corrupt Directional Focus lookups, so we skip.
            if (monitor.IsFallback)
            {
                _logger?.Warn($"DirectionalFocusRegistry: ignored placement for slot={placement.SlotId}, hwnd={placement.Hwnd} – monitor resolve failed (fallback monitor), not mapping");
                continue;
            }

            var workArea = monitor.WorkArea;
            bool customSplit = false;
            if (getSlotRects is not null)
            {
                if (!slotsByMonitor.TryGetValue(workArea, out var slotRects))
                    slotsByMonitor[workArea] = slotRects = getSlotRects(monitor);
                if (!slotRects.ContainsKey(placement.SlotId)) continue;
                customSplit = slotRects.ContainsKey("Custom3");
            }

            var slot = MapSlotId(placement.SlotId, customSplit);
            if (slot is null) continue;

            if (!_plannedSlotsByMonitor.TryGetValue(workArea, out var plans))
                _plannedSlotsByMonitor[workArea] = plans = new Dictionary<IntPtr, WindowPlacement>();
            plans.TryAdd(placement.Hwnd, placement);

            if (!_slotsByMonitor.TryGetValue(workArea, out var slotMap))
            {
                slotMap = new Dictionary<DirectionalFocusSlot, IntPtr>();
                _slotsByMonitor[workArea] = slotMap;
            }

            if (slotMap.TryAdd(slot.Value, placement.Hwnd))
                _logger?.Info($"DirectionalFocusRegistry: mapped slot={slot.Value}, hwnd={placement.Hwnd}, monitor={workArea}");
        }
    }

    public IntPtr? GetHwndForSlot(Rect monitorWorkArea, DirectionalFocusSlot slot)
    {
        if (!_slotsByMonitor.TryGetValue(monitorWorkArea, out var slotMap))
        {
            _logger?.Info($"DirectionalFocusRegistry: no slots for monitor={monitorWorkArea}");
            return null;
        }

        if (!slotMap.TryGetValue(slot, out var hwnd))
        {
            _logger?.Info($"DirectionalFocusRegistry: no window for slot={slot} on monitor={monitorWorkArea}");
            return null;
        }

        if (!_isWindowValid(hwnd))
        {
            _logger?.Warn($"DirectionalFocusRegistry: invalid window handle hwnd={hwnd} for slot={slot}, removing");
            slotMap.Remove(slot);
            return null;
        }

        if (!_isOnCurrentVirtualDesktop(hwnd))
        {
            // The window is on another virtual desktop right now (e.g. the user switched
            // away with Ctrl+Win+Arrow). Do NOT remove the slot: when they come back to
            // this desktop the window will be focusable again without re-arranging.
            _logger?.Info($"DirectionalFocusRegistry: window hwnd={hwnd} for slot={slot} is not on current virtual desktop (kept for return)");
            return null;
        }

        return hwnd;
    }

    public bool HasSlotsForMonitor(Rect monitorWorkArea)
    {
        return _slotsByMonitor.ContainsKey(monitorWorkArea) && _slotsByMonitor[monitorWorkArea].Count > 0;
    }

    /// <summary>
    /// Returns every (slot, hwnd) pair currently populated and valid for a monitor — i.e. hwnd
    /// is a live window on the current virtual desktop. Used by Directional Focus to pick a
    /// fallback slot when the exact requested slot is empty (e.g. fewer than 8 windows, so the
    /// edge-centred slots are unpopulated). Pairs are returned in no particular order.
    /// </summary>
    public IReadOnlyList<(DirectionalFocusSlot Slot, IntPtr Hwnd)> GetAvailableSlots(Rect monitorWorkArea)
    {
        var result = new List<(DirectionalFocusSlot, IntPtr)>();
        if (!_slotsByMonitor.TryGetValue(monitorWorkArea, out var slotMap))
            return result;

        foreach (var kv in slotMap)
        {
            if (!_isWindowValid(kv.Value))
                continue;
            if (!_isOnCurrentVirtualDesktop(kv.Value))
                continue;
            result.Add((kv.Key, kv.Value));
        }
        return result;
    }

    internal static DirectionalFocusSlot? MapSlotId(string slotId, bool customSplit = false)
    {
        return slotId switch
        {
            "A" => DirectionalFocusSlot.TopLeft,
            "B" => DirectionalFocusSlot.BottomRight,
            "C" => DirectionalFocusSlot.TopRight,
            "D" => DirectionalFocusSlot.BottomLeft,
            "E" => DirectionalFocusSlot.TopCenter,
            "F" => DirectionalFocusSlot.BottomCenter,
            "G" => DirectionalFocusSlot.MiddleRight,
            "H" => DirectionalFocusSlot.MiddleLeft,
            "Left" or "Col1" or "Custom1" => DirectionalFocusSlot.MiddleLeft,
            "Right" or "Col3" => DirectionalFocusSlot.MiddleRight,
            "Col2" => DirectionalFocusSlot.TopCenter,
            "Custom2" => customSplit ? DirectionalFocusSlot.TopRight : DirectionalFocusSlot.MiddleRight,
            "Custom3" => DirectionalFocusSlot.BottomRight,
            _ => null
        };
    }

    internal IReadOnlyDictionary<Rect, Dictionary<DirectionalFocusSlot, IntPtr>> GetInternalState() => _slotsByMonitor;
}
