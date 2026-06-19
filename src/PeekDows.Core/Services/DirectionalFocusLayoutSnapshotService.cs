using System;
using System.Collections.Generic;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Reconstructs a slot→hwnd map for a monitor by inspecting the *current*
/// physical positions of windows, without moving anything.
/// </summary>
/// <remarks>
/// Used after a virtual-desktop switch when <see cref="DirectionalFocusRegistry"/>
/// has gone stale: if the windows on the current desktop are still sitting in the
/// ClassicPeekGrid slots, this rebuilds the map so Directional Focus keeps working
/// until the user re-arranges.
///
/// The service never calls <c>SetWindowPos</c> or any placement API. It only reads
/// window rects and compares them (within a tolerance) to the rects the layout
/// engine would have produced.
/// </remarks>
public sealed class DirectionalFocusLayoutSnapshotService
{
    /// <summary>
    /// Default per-edge tolerance (px). Windows may report rects a few px off the
    /// requested placement, and a small drift should not break slot recognition.
    /// </summary>
    public const int DefaultTolerancePx = 40;

    private readonly IMonitorResolver _monitorResolver;
    private readonly LayoutEngine _layoutEngine;
    private readonly Func<IntPtr, Rect> _getWindowRect;
    private readonly Func<IntPtr, bool> _isOnCurrentVirtualDesktop;
    private readonly int _tolerancePx;

    public DirectionalFocusLayoutSnapshotService(
        IMonitorResolver monitorResolver,
        LayoutEngine layoutEngine,
        Func<IntPtr, Rect> getWindowRect,
        Func<IntPtr, bool> isOnCurrentVirtualDesktop,
        int tolerancePx = DefaultTolerancePx,
        FileLogger? logger = null)
    {
        _monitorResolver = monitorResolver;
        _layoutEngine = layoutEngine;
        _getWindowRect = getWindowRect;
        _isOnCurrentVirtualDesktop = isOnCurrentVirtualDesktop;
        _tolerancePx = tolerancePx;
        Logger = logger;
    }

    internal FileLogger? Logger { get; }

    /// <summary>
    /// Builds the slot→hwnd map for a single monitor work area from the given
    /// candidate window handles. Windows whose rect does not match any slot (or
    /// that belong to another monitor / another virtual desktop) are ignored.
    /// </summary>
    public IReadOnlyDictionary<DirectionalFocusSlot, IntPtr> BuildSlotMap(Rect monitorWorkArea, IEnumerable<IntPtr> candidateHwnds)
    {
        var slotRectsById = _layoutEngine.CalculateClassicPeekGridSlotRects(monitorWorkArea);

        var result = new Dictionary<DirectionalFocusSlot, IntPtr>();

        foreach (var hwnd in candidateHwnds)
        {
            if (hwnd == IntPtr.Zero) continue;

            if (!_isOnCurrentVirtualDesktop(hwnd))
            {
                Logger?.Info($"DirectionalFocusLayoutSnapshot: skipping hwnd={hwnd} – not on current virtual desktop");
                continue;
            }

            var rect = _getWindowRect(hwnd);

            // A window physically on a different monitor must never be mapped to
            // this monitor's slots.
            var windowMonitor = _monitorResolver.GetMonitorForWindow(hwnd);
            if (windowMonitor.WorkArea != monitorWorkArea)
            {
                Logger?.Info($"DirectionalFocusLayoutSnapshot: skipping hwnd={hwnd} rect={rect} – on monitor={windowMonitor.WorkArea}, not target monitor={monitorWorkArea}");
                continue;
            }

            foreach (var (slotId, expectedRect) in slotRectsById)
            {
                if (RectApproximatelyEquals(rect, expectedRect, _tolerancePx))
                {
                    var slot = MapSlotId(slotId);
                    if (slot == null) continue;

                    // First matching window wins; later duplicates keep the earliest.
                    if (result.ContainsKey(slot.Value))
                    {
                        Logger?.Info($"DirectionalFocusLayoutSnapshot: slot={slot.Value} already mapped to hwnd={result[slot.Value]}, hwnd={hwnd} ignored");
                        continue;
                    }

                    result[slot.Value] = hwnd;
                    Logger?.Info($"DirectionalFocusLayoutSnapshot: matched hwnd={hwnd} to slot={slot.Value}, rect={rect}");
                    break;
                }
            }

            if (!AnySlotMatches(rect, slotRectsById, _tolerancePx))
            {
                Logger?.Info($"DirectionalFocusLayoutSnapshot: no match for hwnd={hwnd}, rect={rect}");
            }
        }

        Logger?.Info($"DirectionalFocusLayoutSnapshot: rebuild complete monitor={monitorWorkArea}, slots={result.Count}");
        return result;
    }

    /// <summary>
    /// Validates that a single window is still physically sitting in its expected
    /// PeekDows slot, without moving it. Used by the registry fast path so Directional
    /// Focus never trusts a stale slot→hwnd mapping after the user dragged a window away.
    /// </summary>
    /// <returns>
    /// <c>true</c> only when: hwnd is non-zero, on the current virtual desktop, on the
    /// requested monitor, and its current rect matches the slot's expected rect within
    /// the configured tolerance.
    /// </returns>
    public bool IsWindowStillInSlot(IntPtr hwnd, Rect monitorWorkArea, DirectionalFocusSlot slot)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!_isOnCurrentVirtualDesktop(hwnd))
        {
            Logger?.Info($"DirectionalFocusLayoutSnapshot.IsWindowStillInSlot: hwnd={hwnd} not on current virtual desktop");
            return false;
        }

        var rect = _getWindowRect(hwnd);

        // The window must still be physically on the requested monitor.
        var windowMonitor = _monitorResolver.GetMonitorForWindow(hwnd);
        if (windowMonitor.WorkArea != monitorWorkArea)
        {
            Logger?.Info($"DirectionalFocusLayoutSnapshot.IsWindowStillInSlot: hwnd={hwnd} rect={rect} on monitor={windowMonitor.WorkArea}, not target monitor={monitorWorkArea}");
            return false;
        }

        var slotId = MapSlotIdFromSlot(slot);
        if (slotId == null)
        {
            return false;
        }

        var slotRectsById = _layoutEngine.CalculateClassicPeekGridSlotRects(monitorWorkArea);
        var expectedRect = slotRectsById[slotId];

        return RectApproximatelyEquals(rect, expectedRect, _tolerancePx);
    }

    internal static bool RectApproximatelyEquals(Rect actual, Rect expected, int tolerancePx)
    {
        return Math.Abs(actual.Left - expected.Left) <= tolerancePx
            && Math.Abs(actual.Top - expected.Top) <= tolerancePx
            && Math.Abs(actual.Width - expected.Width) <= tolerancePx
            && Math.Abs(actual.Height - expected.Height) <= tolerancePx;
    }

    private static bool AnySlotMatches(Rect rect, IReadOnlyDictionary<string, Rect> slotRectsById, int tolerancePx)
    {
        foreach (var expected in slotRectsById.Values)
        {
            if (RectApproximatelyEquals(rect, expected, tolerancePx)) return true;
        }
        return false;
    }

    private static DirectionalFocusSlot? MapSlotId(string slotId)
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

    private static string? MapSlotIdFromSlot(DirectionalFocusSlot slot)
    {
        return slot switch
        {
            DirectionalFocusSlot.TopLeft => "A",
            DirectionalFocusSlot.BottomRight => "B",
            DirectionalFocusSlot.TopRight => "C",
            DirectionalFocusSlot.BottomLeft => "D",
            DirectionalFocusSlot.TopCenter => "E",
            DirectionalFocusSlot.BottomCenter => "F",
            DirectionalFocusSlot.MiddleRight => "G",
            DirectionalFocusSlot.MiddleLeft => "H",
            _ => null
        };
    }
}
