using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public sealed class DirectionalFocusRegistry
{
    private readonly Dictionary<Rect, Dictionary<DirectionalFocusSlot, IntPtr>> _slotsByMonitor = new();
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

    public void UpdateFromPlacements(IReadOnlyList<WindowPlacement> placements, IMonitorResolver monitorResolver)
    {
        _slotsByMonitor.Clear();
        ApplyPlacements(placements, monitorResolver);
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

    private void ApplyPlacements(IReadOnlyList<WindowPlacement> placements, IMonitorResolver monitorResolver)
    {
        foreach (var placement in placements)
        {
            var slot = MapSlotId(placement.SlotId);
            if (slot == null) continue;

            if (!_isOnCurrentVirtualDesktop(placement.Hwnd))
            {
                _logger?.Info($"DirectionalFocusRegistry: ignored placement for slot={slot.Value}, hwnd={placement.Hwnd} – window not on current virtual desktop");
                continue;
            }

            var monitor = monitorResolver.GetMonitorForWindow(placement.Hwnd);
            var workArea = monitor.WorkArea;

            if (!_slotsByMonitor.TryGetValue(workArea, out var slotMap))
            {
                slotMap = new Dictionary<DirectionalFocusSlot, IntPtr>();
                _slotsByMonitor[workArea] = slotMap;
            }

            slotMap[slot.Value] = placement.Hwnd;
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

    internal static DirectionalFocusSlot? MapSlotId(string slotId)
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
            _ => null
        };
    }

    internal IReadOnlyDictionary<Rect, Dictionary<DirectionalFocusSlot, IntPtr>> GetInternalState() => _slotsByMonitor;
}
