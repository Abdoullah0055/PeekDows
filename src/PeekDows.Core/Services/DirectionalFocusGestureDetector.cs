using System;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public sealed class DirectionalFocusGestureDetector
{
    private const int MinAxialPx = 40;

    public DirectionalFocusSlot? Detect(int anchorX, int anchorY, int currentX, int currentY, int thresholdPx)
    {
        int dx = currentX - anchorX;
        int dy = currentY - anchorY;

        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < thresholdPx)
            return null;

        if (Math.Abs(dx) < MinAxialPx || Math.Abs(dy) < MinAxialPx)
            return null;

        if (dx < 0 && dy < 0) return DirectionalFocusSlot.TopLeft;
        if (dx > 0 && dy < 0) return DirectionalFocusSlot.TopRight;
        if (dx < 0 && dy > 0) return DirectionalFocusSlot.BottomLeft;
        if (dx > 0 && dy > 0) return DirectionalFocusSlot.BottomRight;

        return null;
    }
}
