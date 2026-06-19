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

        // Diagonal gestures take priority and are unchanged: both axes must clear the axial
        // minimum. This keeps A/B/C/D behaviour byte-for-byte identical to before the
        // edge-centred slots were added.
        bool dxIsDiagonal = absDx >= MinAxialPx;
        bool dyIsDiagonal = absDy >= MinAxialPx;

        if (dxIsDiagonal && dyIsDiagonal)
        {
            if (dx < 0 && dy < 0) return DirectionalFocusSlot.TopLeft;
            if (dx > 0 && dy < 0) return DirectionalFocusSlot.TopRight;
            if (dx < 0 && dy > 0) return DirectionalFocusSlot.BottomLeft;
            if (dx > 0 && dy > 0) return DirectionalFocusSlot.BottomRight;
        }

        // Straight (cardinal) gestures target the edge-centred slots E/F/G/H. Only one axis
        // is significant; the other must be small enough that the move reads as clearly
        // vertical or horizontal rather than a shallow diagonal. Anything in between (both
        // axes present but neither dominant enough) is ambiguous → no-op.
        bool dxIsCardinal = absDx >= MinAxialPx;
        bool dyIsCardinal = absDy >= MinAxialPx;

        if (dyIsCardinal && !dxIsDiagonal && absDy >= absDx * CardinalDominanceRatio)
        {
            return dy < 0 ? DirectionalFocusSlot.TopCenter : DirectionalFocusSlot.BottomCenter;
        }

        if (dxIsCardinal && !dyIsDiagonal && absDx >= absDy * CardinalDominanceRatio)
        {
            return dx > 0 ? DirectionalFocusSlot.MiddleRight : DirectionalFocusSlot.MiddleLeft;
        }

        return null;
    }
}
