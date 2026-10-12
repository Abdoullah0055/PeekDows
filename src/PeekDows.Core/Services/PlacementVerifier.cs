using System;
using System.Collections.Generic;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Post-arrange diagnostic: compares where windows were told to go with where
/// they actually are. Distinguishes "we placed wrong" (our bug) from "the app
/// refused/clamped" (min sizes) or "never placed" (skipped). Pure and tested;
/// the caller reads live rects (GetWindowRect) and logs the deviations.
/// Maximized windows are always skipped: their async SW_RESTORE has not
/// settled at verify time, so any mismatch would be a false positive.
/// </summary>
public sealed record PlacementDeviation(IntPtr Hwnd, Rect Expected, Rect Actual);

public static class PlacementVerifier
{
    public const int TolerancePx = 4;

    public static IReadOnlyList<PlacementDeviation> FindDeviations(
        IReadOnlyList<WindowPlacement> placements,
        Func<IntPtr, Rect?> getCurrentRect,
        Func<IntPtr, bool> wasMaximized)
    {
        var result = new List<PlacementDeviation>();
        if (placements == null || placements.Count == 0)
            return result;

        foreach (var p in placements)
        {
            try
            {
                if (wasMaximized != null && wasMaximized(p.Hwnd))
                    continue;
                Rect? actual = getCurrentRect != null ? getCurrentRect(p.Hwnd) : null;
                if (actual == null)
                    continue;
                if (Differs(p.TargetRect, actual.Value, TolerancePx))
                    result.Add(new PlacementDeviation(p.Hwnd, p.TargetRect, actual.Value));
            }
            catch
            {
                // One unreadable window must never fail the whole verification.
            }
        }
        return result;
    }

    private static bool Differs(Rect expected, Rect actual, int tolerance)
    {
        return Math.Abs(expected.Left - actual.Left) > tolerance
            || Math.Abs(expected.Top - actual.Top) > tolerance
            || Math.Abs(expected.Right - actual.Right) > tolerance
            || Math.Abs(expected.Bottom - actual.Bottom) > tolerance;
    }
}
