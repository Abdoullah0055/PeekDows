using System;
using System.Collections.Generic;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Invisible DWM frame margins of one window, in pixels per side. Top-level
/// windows carry invisible resize borders (~7px/side on Win10, varies with DPI
/// and Windows version); SetWindowPos positions the OUTER rect, so adjacent
/// slots show a visible strip unless targets are inflated by these margins.
/// All zeros = borderless window, nothing to compensate.
/// </summary>
public readonly record struct FrameMargins(int Left, int Top, int Right, int Bottom)
{
    public static readonly FrameMargins Zero = new(0, 0, 0, 0);
}

/// <summary>
/// Pure frame compensation: inflates slot target rects by measured invisible
/// margins so the VISIBLE areas tile edge-to-edge in every layout mode.
/// </summary>
public static class FrameCompensation
{
    /// <summary>
    /// Derives margins from the outer rect (GetWindowRect) and the visible
    /// rect (DwmGetWindowAttribute EXTENDED_FRAME_BOUNDS). Negative diffs
    /// (rounded corners, odd frames) clamp to zero — never shrinks.
    /// </summary>
    public static FrameMargins Measure(Rect outer, Rect visible)
    {
        return new FrameMargins(
            Math.Max(0, visible.Left - outer.Left),
            Math.Max(0, visible.Top - outer.Top),
            Math.Max(0, outer.Right - visible.Right),
            Math.Max(0, outer.Bottom - visible.Bottom));
    }

    /// <summary>
    /// Inflates a slot target so its visible area lands exactly on it.
    /// Maximized-kind placements are returned untouched (native maximize
    /// handles its own frame; never inflate those).
    /// </summary>
    public static Rect Compensate(Rect target, FrameMargins margins)
    {
        return new Rect(
            target.Left - margins.Left,
            target.Top - margins.Top,
            target.Width + margins.Left + margins.Right,
            target.Height + margins.Top + margins.Bottom);
    }

    /// <summary>
    /// Applies compensation to every Reposition placement; Maximize placements
    /// pass through untouched. Margins come from the caller (live DWM measure
    /// per hwnd); missing entries mean zero margins.
    /// </summary>
    public static IReadOnlyList<WindowPlacement> WithCompensatedRects(
        IReadOnlyList<WindowPlacement> placements,
        IReadOnlyDictionary<IntPtr, FrameMargins> marginsByHwnd)
    {
        var result = new List<WindowPlacement>(placements.Count);
        foreach (var p in placements)
        {
            if (p.Kind == PlacementKind.Maximize
                || marginsByHwnd == null
                || !marginsByHwnd.TryGetValue(p.Hwnd, out var margins)
                || (margins.Left == 0 && margins.Top == 0 && margins.Right == 0 && margins.Bottom == 0))
            {
                result.Add(p);
                continue;
            }
            result.Add(new WindowPlacement
            {
                Hwnd = p.Hwnd,
                SlotId = p.SlotId,
                TargetRect = Compensate(p.TargetRect, margins),
                Activate = p.Activate,
                PreserveZOrder = p.PreserveZOrder,
                BringToFront = p.BringToFront,
                Kind = p.Kind,
            });
        }
        return result;
    }
}
