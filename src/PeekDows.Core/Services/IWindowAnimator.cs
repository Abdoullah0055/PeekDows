using System;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Animation seam consumed by <see cref="WindowPlacementService"/> (Core) and implemented
/// by the WinForms-timer-driven <c>WindowAnimationService</c> in the App project (Core
/// cannot reference WinForms). Implementations must never block the caller.
/// </summary>
public interface IWindowAnimator
{
    /// <summary>
    /// Begins animating <paramref name="hwnd"/> from its current rect toward
    /// <paramref name="targetRect"/>. <see cref="PlacementKind.Maximize"/> tweens toward
    /// the rect and issues the native maximize on the final frame (tween-then-snap).
    /// Returns false when the animation cannot start (caller must place instantly instead).
    /// Starting a tween for an hwnd that is already animating supersedes the old tween.
    /// </summary>
    bool Begin(IntPtr hwnd, Rect targetRect, PlacementKind kind, bool bringToFront = false);

    /// <summary>True while <paramref name="hwnd"/> has an in-flight tween.</summary>
    bool IsAnimating(IntPtr hwnd);

    /// <summary>Snaps every in-flight tween to its final target immediately (used on pause).</summary>
    void SnapAllToTarget();
}
