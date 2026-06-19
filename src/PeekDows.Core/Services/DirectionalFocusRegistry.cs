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

    public DirectionalFocusRegistry(FileLogger? logger = null) : this(hwnd => NativeMethods.IsWindow(hwnd), logger) { }

    internal DirectionalFocusRegistry(Func<IntPtr, bool> isWindowValid, FileLogger? logger = null)
    {
        _isWindowValid = isWindowValid;
        _logger = logger;
    }

    public void UpdateFromPlacements(IReadOnlyList<WindowPlacement> placements, IMonitorResolver monitorResolver)
    {
        _slotsByMonitor.Clear();

        foreach (var placement in placements)
        {
            var slot = MapSlotId(placement.SlotId);
            if (slot == null) continue;

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
            _ => null
        };
    }

    internal IReadOnlyDictionary<Rect, Dictionary<DirectionalFocusSlot, IntPtr>> GetInternalState() => _slotsByMonitor;
}
