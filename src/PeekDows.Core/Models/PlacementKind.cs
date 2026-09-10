namespace PeekDows.Core.Models;

/// <summary>
/// What a placement asks the placement service / animator to do with the window.
/// </summary>
public enum PlacementKind
{
    /// <summary>Move/resize the window into TargetRect.</summary>
    Reposition = 0,

    /// <summary>
    /// Maximize the window natively. TargetRect carries the monitor work area and is used
    /// as the tween destination by the animator (tween-then-snap).
    /// </summary>
    Maximize,

    /// <summary>
    /// Window is currently maximized: restore it, then animate it into TargetRect.
    /// </summary>
    RestoreAndReposition
}
