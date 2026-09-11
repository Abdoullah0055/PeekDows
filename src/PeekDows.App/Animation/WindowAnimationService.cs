using System;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Animation;

/// <summary>
/// Win32 seam for the window animator so unit tests can drive tween behavior without
/// touching real windows. Every method must be non-blocking (async Win32 flags inside).
/// </summary>
public interface IWindowAnimationApi
{
    bool IsWindow(IntPtr hwnd);
    bool IsZoomed(IntPtr hwnd);

    /// <summary>
    /// Moves/resizes the window to rect, asynchronously (SWP_ASYNCWINDOWPOS). When
    /// <paramref name="bringToFront"/> is false the call preserves Z-order
    /// (SWP_NOZORDER); when true it inserts at HWND_TOP to raise above peers
    /// (still NOACTIVATE so focus is not stolen).
    /// </summary>
    bool SetWindowPos(IntPtr hwnd, Rect rect, bool bringToFront = false);

    /// <summary>Natively maximizes via non-blocking ShowWindowAsync(SW_MAXIMIZE).</summary>
    bool Maximize(IntPtr hwnd);

    /// <summary>
    /// Non-blocking restore (ShowWindowAsync SW_RESTORE). Used for
    /// RestoreAndReposition: the window is restored then glided from its
    /// monitor's WorkArea into the slot.
    /// </summary>
    bool RestoreWindow(IntPtr hwnd);

    /// <summary>Returns the monitor WorkArea for the window (fallback 1920x1080 if unknown).</summary>
    Rect GetWorkAreaForWindow(IntPtr hwnd);

    /// <summary>Reads the window's current rect (GetWindowRect). False when unavailable.</summary>
    bool TryGetRect(IntPtr hwnd, out Rect rect);

    /// <summary>
    /// True while a physical mouse button (left or right) is held. Used by grab detection:
    /// a window drifting off its commanded path is only a user grab when the user is
    /// actually dragging — async SetWindowPos lag produces the same drift without buttons.
    /// </summary>
    bool AreMouseButtonsPressed();

    /// <summary>Current cursor position in screen coordinates.</summary>
    System.Drawing.Point GetCursorPosition();

    /// <summary>Window handle at the given screen point (WindowFromPoint). IntPtr.Zero if none.</summary>
    IntPtr WindowFromPoint(System.Drawing.Point pt);
}

public sealed class Win32AnimationApi : IWindowAnimationApi
{
    public bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);
    public bool IsZoomed(IntPtr hwnd) => NativeMethods.IsZoomed(hwnd);

    public bool SetWindowPos(IntPtr hwnd, Rect rect, bool bringToFront = false)
    {
        var insertAfter = bringToFront ? NativeMethods.HWND_TOP : NativeMethods.HWND_NOTOPMOST;
        uint flags = (uint)(NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_ASYNCWINDOWPOS);
        if (!bringToFront) flags |= (uint)NativeMethods.SWP_NOZORDER;
        return NativeMethods.SetWindowPos(hwnd, insertAfter, rect.Left, rect.Top, rect.Width, rect.Height, flags);
    }

    public bool Maximize(IntPtr hwnd) => NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MAXIMIZE);
    public bool RestoreWindow(IntPtr hwnd) => NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
    public Rect GetWorkAreaForWindow(IntPtr hwnd)
    {
        try
        {
            var svc = new MonitorService(null);
            var mi = svc.GetMonitorForWindow(hwnd);
            if (!mi.IsFallback) return mi.WorkArea;
        }
        catch { }
        return new Rect(0, 0, 1920, 1080);
    }

    public bool TryGetRect(IntPtr hwnd, out Rect rect)
    {
        if (NativeMethods.GetWindowRect(hwnd, out var nativeRect))
        {
            rect = new Rect(nativeRect.left, nativeRect.top, nativeRect.right - nativeRect.left, nativeRect.bottom - nativeRect.top);
            return true;
        }
        rect = default;
        return false;
    }

    public bool AreMouseButtonsPressed()
    {
        const int VK_LBUTTON = 0x01;
        const int VK_RBUTTON = 0x02;
        return (NativeMethods.GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
    }

    public System.Drawing.Point GetCursorPosition() => System.Windows.Forms.Cursor.Position;

    public IntPtr WindowFromPoint(System.Drawing.Point pt)
    {
        try { return NativeMethods.WindowFromPoint(new System.Drawing.Point(pt.X, pt.Y)); } catch { return IntPtr.Zero; }
    }
}

/// <summary>
/// Central window-transition animator (spec 2026-09-09). One shared ~60fps WinForms timer
/// drives every in-flight tween; there is never one timer per window. Motion is ease-out
/// cubic over 150ms with per-frame non-blocking SetWindowPos (no-activate, no z-order).
/// Hardening rules:
/// - windows on the UnstableWindowTracker are marked when an animated SetWindowPos fails;
/// - user grabs (real rect deviating from the expected interpolated rect beyond a few px,
///   outside a short post-restore grace period) cancel the tween — PeekDows never fights
///   the user;
/// - vanishing windows cancel their tween;
/// - SnapAllToTarget() is called on pause: every tween lands instantly on its target.
/// </summary>
public sealed class WindowAnimationService : IWindowAnimator, IDisposable
{
    internal const int TickIntervalMs = 16;
    internal const int DurationMs = 150;
    internal const int UserGrabDeviationPx = 6;

    /// <summary>
    /// Grace period right after Begin during which deviation does NOT cancel the tween:
    /// an async SW_RESTORE lands a few ms after the request and temporarily moves the
    /// window in ways that look like a grab but are not.
    /// </summary>
    internal const int GrabDetectionGraceMs = 50;

    internal const int SlowWin32CallThresholdMs = 500;

    private sealed class Tween
    {
        public Rect From;
        public Rect To;
        public PlacementKind Kind;
        public bool BringToFront;
        public DateTime StartedAt;

        /// <summary>
        /// The rect we last commanded via SetWindowPos. Grab detection compares the real
        /// rect against THIS (not against the current tick's expected position): a window
        /// legitimately lags one async frame behind, so "not yet at the new expected spot"
        /// is normal, while "not where we last put it" means someone else moved it.
        /// </summary>
        public Rect LastWritten;
    }

    private readonly Dictionary<IntPtr, Tween> _tweens = new();
    private readonly IWindowAnimationApi _api;
    private readonly Func<DateTime> _nowProvider;
    private readonly UnstableWindowTracker? _tracker;
    private readonly FileLogger? _logger;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _disposed;

    public WindowAnimationService(IWindowAnimationApi api, UnstableWindowTracker? tracker = null, FileLogger? logger = null)
        : this(api, () => DateTime.UtcNow, tracker, logger)
    {
    }

    internal WindowAnimationService(IWindowAnimationApi api, Func<DateTime> nowProvider, UnstableWindowTracker? tracker = null, FileLogger? logger = null)
    {
        _api = api;
        _nowProvider = nowProvider;
        _tracker = tracker;
        _logger = logger;
        _timer = new System.Windows.Forms.Timer { Interval = TickIntervalMs };
        _timer.Tick += OnTimerTick;
    }

    public bool Begin(IntPtr hwnd, Rect targetRect, PlacementKind kind, bool bringToFront = false)
    {
        if (hwnd == IntPtr.Zero || !_api.IsWindow(hwnd))
        {
            return false;
        }

        if (kind == PlacementKind.Maximize && _api.IsZoomed(hwnd))
        {
            // Already maximized: nothing to animate, report success (steady state).
            return true;
        }

        Rect from;
        if (kind == PlacementKind.RestoreAndReposition)
        {
            // For a maximized→slot transition the current rect is the maximized rect.
            // Tweening from it would glide from fullscreen; spec wants restore→WorkArea→glide.
            // Issue the non-blocking restore immediately and use the monitor WorkArea as From.
            _api.RestoreWindow(hwnd);
            from = _api.GetWorkAreaForWindow(hwnd);
            _logger?.Info($"Animation RestoreAndReposition: hwnd={hwnd}, WorkArea as From={from}, to={targetRect}");
        }
        else if (!_api.TryGetRect(hwnd, out from))
        {
            return false;
        }

        if (MaxComponentDeviation(from, targetRect) <= 1)
        {
            return true; // already there
        }

        _tweens[hwnd] = new Tween { From = from, To = targetRect, Kind = kind, BringToFront = bringToFront, StartedAt = _nowProvider(), LastWritten = from };
        if (!_timer.Enabled)
        {
            _timer.Start();
        }

        _logger?.Info($"Animation begun: hwnd={hwnd}, kind={kind}, bringToFront={bringToFront}, from={from}, to={targetRect}");
        return true;
    }

    public bool IsAnimating(IntPtr hwnd) => _tweens.ContainsKey(hwnd);

    public void SnapAllToTarget()
    {
        if (_tweens.Count == 0) return;

        foreach (var (hwnd, tween) in _tweens)
        {
            if (!_api.IsWindow(hwnd)) continue;

            if (tween.Kind == PlacementKind.Maximize)
            {
                _api.Maximize(hwnd);
            }
            else
            {
                _api.SetWindowPos(hwnd, tween.To, tween.BringToFront);
            }
        }

        _tweens.Clear();
        _timer.Stop();
        _logger?.Info("Animation: all tweens snapped to target (pause/supersede)");
    }

    internal void Tick()
    {
        var now = _nowProvider();
        var finished = new List<IntPtr>();

        foreach (var (hwnd, tween) in _tweens)
        {
            if (!_api.IsWindow(hwnd))
            {
                finished.Add(hwnd);
                continue;
            }

            double t = (now - tween.StartedAt).TotalMilliseconds / DurationMs;
            if (t >= 1.0)
            {
                FinishTween(hwnd, tween);
                finished.Add(hwnd);
                continue;
            }

            bool inGrace = (now - tween.StartedAt).TotalMilliseconds < GrabDetectionGraceMs;
            var expected = LerpRect(tween.From, tween.To, EaseOutCubic(t));

            // Grab detection needs BOTH signals: the window off its commanded path AND a
            // mouse button held. Async SetWindowPos lag alone mimics the first condition
            // (posted moves sit in the target's queue for several frames) — without a held
            // button it is never a grab.
            // B1 fix: DWM invisible border + global drag false positive.
            // We require (a) a real deviation beyond threshold, (b) a mouse button held,
            // AND (c) the cursor actually over the animated window. Without (c), dragging
            // another window cancels ALL tweens (the old bug). We also tolerate async
            // SetWindowPos lag: the window may lag one frame behind LastWritten — so we
            // compare against both LastWritten and the current expected rect, and only
            // treat it as a grab when BOTH deviate.
            if (!inGrace && _api.AreMouseButtonsPressed() && _api.TryGetRect(hwnd, out var actual)
                && MaxComponentDeviation(actual, tween.LastWritten) > UserGrabDeviationPx
                && MaxComponentDeviation(actual, expected) > UserGrabDeviationPx)
            {
                // Only cancel if cursor is actually over this hwnd (not dragging elsewhere).
                var cursor = _api.GetCursorPosition();
                var underCursor = _api.WindowFromPoint(cursor);
                if (underCursor == hwnd || IsAncestorOrSelf(underCursor, hwnd))
                {
                    _logger?.Info($"Animation cancelled (user grab): hwnd={hwnd}");
                    finished.Add(hwnd);
                    continue;
                }
            }

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool ok = _api.SetWindowPos(hwnd, expected, tween.BringToFront);
            long elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs >= SlowWin32CallThresholdMs)
            {
                _logger?.Warn($"Animated SetWindowPos slow ({elapsedMs}ms), marking unstable: hwnd={hwnd}");
                _tracker?.MarkUnstable(hwnd, now, $"slow-animated-setwindowpos-{elapsedMs}ms");
                finished.Add(hwnd);
                continue;
            }
            if (!ok)
            {
                _logger?.Warn($"Animated SetWindowPos failed, cancelling tween: hwnd={hwnd}");
                _tracker?.MarkUnstable(hwnd, now, "animated-setwindowpos-failed");
                finished.Add(hwnd);
                continue;
            }

            tween.LastWritten = expected;
        }

        foreach (var hwnd in finished)
        {
            _tweens.Remove(hwnd);
        }

        if (_tweens.Count == 0)
        {
            _timer.Stop();
        }
    }

    private void FinishTween(IntPtr hwnd, Tween tween)
    {
        if (tween.Kind == PlacementKind.Maximize)
        {
            _logger?.Info($"Animation final frame: native maximize hwnd={hwnd}");
            _api.Maximize(hwnd);
        }
        else
        {
            // Exact final rect — no rounding drift. Preserve BringToFront for focus placement.
            _api.SetWindowPos(hwnd, tween.To, tween.BringToFront);
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            _logger?.Error("WindowAnimationService tick error", ex);
        }
    }

    internal static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3);

    internal static Rect LerpRect(Rect from, Rect to, double t)
    {
        return new Rect(
            (int)Math.Round(from.Left + (to.Left - from.Left) * t),
            (int)Math.Round(from.Top + (to.Top - from.Top) * t),
            (int)Math.Round(from.Width + (to.Width - from.Width) * t),
            (int)Math.Round(from.Height + (to.Height - from.Height) * t));
    }

    internal static int MaxComponentDeviation(Rect a, Rect b)
    {
        return Math.Max(
            Math.Max(Math.Abs(a.Left - b.Left), Math.Abs(a.Top - b.Top)),
            Math.Max(Math.Abs(a.Width - b.Width), Math.Abs(a.Height - b.Height)));
    }

    private bool IsAncestorOrSelf(IntPtr child, IntPtr ancestor)
    {
        try
        {
            var cur = child;
            while (cur != IntPtr.Zero)
            {
                if (cur == ancestor) return true;
                cur = NativeMethods.GetParent(cur);
            }
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { SnapAllToTarget(); } catch { }
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer.Dispose();
    }
}
