namespace PeekDows.Core.Models;

/// <summary>
/// The eight slots Directional Focus can target, matching the eight-slot ClassicPeekGrid
/// layout. The four corner slots (A–D) are reached by diagonal gestures; the four edge-centred
/// slots (E–H) are reached by straight vertical/horizontal gestures.
/// </summary>
public enum DirectionalFocusSlot
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCenter,
    BottomCenter,
    MiddleRight,
    MiddleLeft
}
