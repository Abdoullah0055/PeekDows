using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Pure geometry for the HintOverlay arrow HUD. Dependency-free on purpose:
/// Core does not reference System.Drawing, so points are plain (X, Y) tuples
/// and the WinForms layer converts them to <c>PointF</c> when painting.
/// Both render paths (layered-window bitmap and keyed-fallback OnPaint) share
/// this code, which guarantees identical arrow placement.
/// </summary>
public static class ArrowGeometry
{
    /// <summary>
    /// Canonical slot order (corners first, then edges), matching the overlay
    /// paint order. Output arrows always follow this order regardless of the
    /// input enumeration order, so callers can zip results with the ordered slots.
    /// </summary>
    public static readonly DirectionalFocusSlot[] SlotOrder =
    [
        DirectionalFocusSlot.TopLeft,
        DirectionalFocusSlot.TopCenter,
        DirectionalFocusSlot.TopRight,
        DirectionalFocusSlot.MiddleRight,
        DirectionalFocusSlot.BottomRight,
        DirectionalFocusSlot.BottomCenter,
        DirectionalFocusSlot.BottomLeft,
        DirectionalFocusSlot.MiddleLeft,
    ];

    /// <summary>
    /// Unit direction vector per slot (y down to match screen coordinates).
    /// Mirrors the convention used by DirectionalFocusService.SlotDirection.
    /// </summary>
    public static (float Dx, float Dy) Direction(DirectionalFocusSlot slot)
    {
        double angleDeg = slot switch
        {
            DirectionalFocusSlot.MiddleRight => 0,
            DirectionalFocusSlot.BottomRight => 45,
            DirectionalFocusSlot.BottomCenter => 90,
            DirectionalFocusSlot.BottomLeft => 135,
            DirectionalFocusSlot.MiddleLeft => 180,
            DirectionalFocusSlot.TopLeft => 225,
            DirectionalFocusSlot.TopCenter => 270,
            DirectionalFocusSlot.TopRight => 315,
            _ => 0,
        };
        double rad = angleDeg * Math.PI / 180.0;
        return ((float)Math.Cos(rad), (float)Math.Sin(rad));
    }

    /// <summary>
    /// Computes one arrow per populated slot: shaft start (P1, on the inner
    /// radius), Tip (reach + 2px overshoot past the nominal reach, scaled) and
    /// the 3-point head triangle (tip first). All lengths are given in unscaled
    /// pixels; <paramref name="scale"/> (DPI factor) is applied here so every
    /// consumer renders the exact same geometry.
    /// </summary>
    public static IReadOnlyList<((float X, float Y) P1, (float X, float Y) Tip, (float X, float Y)[] Head)> ComputeArrows(
        IEnumerable<DirectionalFocusSlot> populated,
        DirectionalFocusSlot? active,
        float center,
        float inner,
        float reach,
        float activeBonus,
        float headLen,
        float headHalf,
        float scale)
    {
        var result = new List<((float X, float Y) P1, (float X, float Y) Tip, (float X, float Y)[] Head)>();
        if (populated is null)
            return result;

        var set = new HashSet<DirectionalFocusSlot>(populated);
        foreach (var slot in SlotOrder)
        {
            if (!set.Contains(slot))
                continue;

            bool isActive = active == slot;
            float r = (reach + (isActive ? activeBonus : 0f)) * scale;
            float innerScaled = inner * scale;
            float overshoot = 2f * scale;
            var (dx, dy) = Direction(slot);

            var p1 = (X: center + dx * innerScaled, Y: center + dy * innerScaled);
            var tip = (X: center + dx * (r + overshoot), Y: center + dy * (r + overshoot));
            float bx = center + dx * (r + overshoot - headLen * scale);
            float by = center + dy * (r + overshoot - headLen * scale);
            float px = -dy, py = dx;
            var head = new (float X, float Y)[]
            {
                tip,
                (bx + px * headHalf * scale, by + py * headHalf * scale),
                (bx - px * headHalf * scale, by - py * headHalf * scale),
            };
            result.Add((p1, tip, head));
        }

        return result;
    }
}
