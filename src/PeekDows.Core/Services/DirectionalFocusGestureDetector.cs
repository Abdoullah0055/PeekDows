using System;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public sealed class DirectionalFocusGestureDetector
{
    private const int MinAxialPx = 40;

    /// <summary>
    /// How much larger the dominant axis must be than the off-axis component for a straight
    /// (vertical/horizontal) gesture to count as unambiguous. With a 2× ratio a 45°-ish
    /// drift still lands on a diagonal, while a clearly vertical or horizontal move targets
    /// an edge-centred slot. Keeps the diagonal path exclusive of the cardinal path.
    /// </summary>
    private const double CardinalDominanceRatio = 2.0;

    public DirectionalFocusSlot? Detect(int anchorX, int anchorY, int currentX, int currentY, int thresholdPx)
    {
        int dx = currentX - anchorX;
        int dy = currentY - anchorY;

        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < thresholdPx)
            return null;

        int absDx = Math.Abs(dx);
        int absDy = Math.Abs(dy);

        // A straight (cardinal) gesture wins when one axis clearly dominates the other by
        // at least CardinalDominanceRatio (2×) AND that dominant axis clears the axial
        // minimum. This is tested BEFORE the diagonal branch so a move like (dx=50, dy=200)
        // — where both axes happen to exceed MinAxialPx — is still read as a vertical
        // gesture (BottomCenter), not a corner diagonal.
        //
        // Ordering: 1) vertical, 2) horizontal, 3) diagonal, 4) no-op.
        bool dyDominates = absDy >= absDx * CardinalDominanceRatio;
        bool dxDominates = absDx >= absDy * CardinalDominanceRatio;

        if (dyDominates && absDy >= MinAxialPx)
        {
            return dy < 0 ? DirectionalFocusSlot.TopCenter : DirectionalFocusSlot.BottomCenter;
        }

        if (dxDominates && absDx >= MinAxialPx)
        {
            return dx > 0 ? DirectionalFocusSlot.MiddleRight : DirectionalFocusSlot.MiddleLeft;
        }

        // Neither axis dominates 2×: if both axes are still significant this is a genuine
        // diagonal corner gesture (A/B/C/D). Anything else is ambiguous and a no-op.
        if (absDx >= MinAxialPx && absDy >= MinAxialPx)
        {
            if (dx < 0 && dy < 0) return DirectionalFocusSlot.TopLeft;
            if (dx > 0 && dy < 0) return DirectionalFocusSlot.TopRight;
            if (dx < 0 && dy > 0) return DirectionalFocusSlot.BottomLeft;
            if (dx > 0 && dy > 0) return DirectionalFocusSlot.BottomRight;
        }

        return null;
    }
}
