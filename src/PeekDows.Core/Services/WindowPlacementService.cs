using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public sealed class WindowPlacementService
{
    private const int SlowWin32CallThresholdMs = 500;

    private readonly IWindowPositioner _positioner;
    private readonly Func<IntPtr, bool> _isWindowValid;
    private readonly Func<IntPtr, bool> _isWindowMaximized;
    private readonly FileLogger? _logger;
    private readonly Func<IntPtr, bool>? _isResponsive;
    private readonly Func<IntPtr, int, bool> _showWindowAsync;
    private readonly UnstableWindowTracker? _tracker;
    private readonly Func<DateTime> _nowProvider;
    private readonly IWindowAnimator? _animator;
    private readonly Func<bool>? _shouldAnimate;

    public WindowPlacementService() : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), hwnd => NativeMethods.IsZoomed(hwnd), null) { }

    public WindowPlacementService(FileLogger logger) : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), hwnd => NativeMethods.IsZoomed(hwnd), logger) { }

    public WindowPlacementService(FileLogger logger, UnstableWindowTracker tracker)
        : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), hwnd => NativeMethods.IsZoomed(hwnd),
               logger, tracker, hwnd => IsResponsiveProbe(hwnd), null)
    {
    }

    public WindowPlacementService(FileLogger logger, UnstableWindowTracker tracker, IWindowAnimator animator, Func<bool> shouldAnimate)
        : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), hwnd => NativeMethods.IsZoomed(hwnd),
               logger, tracker, hwnd => IsResponsiveProbe(hwnd), null, null, animator, shouldAnimate)
    {
    }

    internal WindowPlacementService(IWindowPositioner positioner) : this(positioner, _ => true, _ => false, null) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid) : this(positioner, isWindowValid, _ => false, null) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid, FileLogger? logger) : this(positioner, isWindowValid, _ => false, logger) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid, Func<IntPtr, bool> isWindowMaximized, FileLogger? logger)
        : this(positioner, isWindowValid, isWindowMaximized, logger, null, null, null)
    {
    }

    /// <summary>
    /// Internal master ctor. The Win32-abstracting seams (<paramref name="isResponsive"/>,
    /// <paramref name="showWindowAsync"/>) and the <paramref name="tracker"/>/clock enable
    /// both defensive behaviour at runtime and deterministic unit testing.
    /// </summary>
    internal WindowPlacementService(
        IWindowPositioner positioner,
        Func<IntPtr, bool> isWindowValid,
        Func<IntPtr, bool> isWindowMaximized,
        FileLogger? logger,
        UnstableWindowTracker? tracker,
        Func<IntPtr, bool>? isResponsive,
        Func<IntPtr, int, bool>? showWindowAsync,
        Func<DateTime>? nowProvider = null,
        IWindowAnimator? animator = null,
        Func<bool>? shouldAnimate = null)
    {
        _positioner = positioner;
        _isWindowValid = isWindowValid;
        _isWindowMaximized = isWindowMaximized;
        _logger = logger;
        _tracker = tracker;
        _isResponsive = isResponsive;
        _showWindowAsync = showWindowAsync ?? ((hwnd, cmd) => NativeMethods.ShowWindowAsync(hwnd, cmd));
        _nowProvider = nowProvider ?? (() => DateTime.UtcNow);
        _animator = animator;
        _shouldAnimate = shouldAnimate;
    }

    /// <summary>
    /// Production responsiveness probe used by the default ctor wiring. Bounded via
    /// SendMessageTimeout(SMTO_ABORTIFHUNG) so a hung window returns false quickly instead
    /// of blocking. Kept here so the <see cref="WindowActivationService"/> and the placement
    /// service share one probe definition.
    /// </summary>
    private static bool IsResponsiveProbe(IntPtr hwnd)
    {
        IntPtr result = NativeMethods.SendMessageTimeout(
            hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG, 200, out _);
        return result != IntPtr.Zero;
    }

    public PlacementResult ApplyPlacements(IReadOnlyList<WindowPlacement> placements)
    {
        if (placements == null || placements.Count == 0)
        {
            _logger?.Info("ApplyPlacements: no placements to apply");
            return new PlacementResult
            {
                AttemptedCount = 0,
                SucceededCount = 0,
                FailedCount = 0
            };
        }

        _logger?.Info($"ApplyPlacements started: {placements.Count} placement(s)");

        int succeeded = 0;
        int failed = 0;
        var errors = new List<string>();
        var succeededHwnds = new List<IntPtr>();

        // P-B6 fix: avoid LINQ alloc (Where+ToList+FirstOrDefault) for ≤8 items.
        WindowPlacement? focus = null;
        foreach (var p in placements) if (p.BringToFront) { focus = p; break; }
        foreach (var placement in placements)
        {
            if (placement.BringToFront) continue;
            TryApplyPlacement(placement, ref succeeded, ref failed, errors, succeededHwnds);
        }
        if (focus != null)
        {
            TryApplyPlacement(focus, ref succeeded, ref failed, errors, succeededHwnds);
        }

        _logger?.Info($"ApplyPlacements completed: attempted={placements.Count}, succeeded={succeeded}, failed={failed}");

        return new PlacementResult
        {
            AttemptedCount = placements.Count,
            SucceededCount = succeeded,
            FailedCount = failed,
            Errors = errors,
            SucceededHwnds = succeededHwnds
        };
    }

    private bool TryApplyPlacement(WindowPlacement placement, ref int succeeded, ref int failed, List<string> errors, List<IntPtr> succeededHwnds)
    {
        try
        {
            _logger?.Info($"Attempting placement: hwnd={placement.Hwnd}, slot={placement.SlotId}, rect={placement.TargetRect}, bringToFront={placement.BringToFront}");

            if (placement.Hwnd == IntPtr.Zero)
            {
                failed++;
                var msg = $"Invalid hwnd for slot {placement.SlotId}";
                errors.Add(msg);
                _logger?.Warn($"Placement skipped: {msg}");
                return false;
            }

            if (!_isWindowValid(placement.Hwnd))
            {
                failed++;
                var msg = $"Window no longer exists: hwnd={placement.Hwnd}, slot={placement.SlotId}";
                errors.Add(msg);
                _logger?.Warn($"Placement skipped: {msg}");
                return false;
            }

            if (placement.TargetRect.Width <= 0 || placement.TargetRect.Height <= 0)
            {
                failed++;
                var msg = $"Invalid rect for slot {placement.SlotId}: {placement.TargetRect}";
                errors.Add(msg);
                _logger?.Warn($"Placement skipped: {msg}");
                return false;
            }

            if (placement.Kind == PlacementKind.Maximize)
            {
                if (_isWindowMaximized(placement.Hwnd))
                {
                    // Steady state: the lone window is already maximized by us. Nothing to do.
                    _logger?.Info($"Placement skipped (already maximized): hwnd={placement.Hwnd}, slot={placement.SlotId}");
                    succeeded++;
                    succeededHwnds.Add(placement.Hwnd);
                    return true;
                }
                // Not maximized yet: fall through — the maximize is issued by the animator
                // (final frame) or the instant path below. Never run the restore path here.
            }
            else if (_isWindowMaximized(placement.Hwnd))
            {
                _logger?.Info($"Maximized window detected: hwnd={placement.Hwnd}, slot={placement.SlotId}");
                _logger?.Info($"Restoring maximized window before placement: hwnd={placement.Hwnd}, slot={placement.SlotId}");
                // ShowWindowAsync posts the restore without waiting for the target thread,
                // so a hung window cannot block arrange here.
                bool restoreResult = _showWindowAsync(placement.Hwnd, NativeMethods.SW_RESTORE);
                _logger?.Info($"ShowWindowAsync(SW_RESTORE) result={restoreResult} for hwnd={placement.Hwnd}");
            }

            // Unstable-window circuit breaker: a window that recently hung, blocked a
            // SetWindowPos, or failed monitor resolution is skipped entirely for the cooldown
            // window. This is the primary freeze prevention for arrange.
            var now = _nowProvider();
            if (_tracker is not null && _tracker.IsUnstable(placement.Hwnd, now))
            {
                _tracker.TryGetReason(placement.Hwnd, out var reason);
                failed++;
                var msg = $"Placement skipped: hwnd in unstable cooldown, hwnd={placement.Hwnd}, slot={placement.SlotId}, reason={reason ?? "unknown"}";
                errors.Add(msg);
                _logger?.Warn(msg);
                return false;
            }

            // Hung-window probe: never issue a blocking SetWindowPos against a window whose
            // thread is not pumping messages. The probe is bounded (~200ms) so PeekDows's UI
            // thread can never stall on a frozen target.
            if (_isResponsive is not null && !_isResponsive(placement.Hwnd))
            {
                _tracker?.MarkUnstable(placement.Hwnd, now, "not-responding-placement");
                failed++;
                var msg = $"Placement skipped: target window not responding, hwnd={placement.Hwnd}, slot={placement.SlotId}";
                errors.Add(msg);
                _logger?.Warn(msg);
                return false;
            }

            // Animated path: hand the placement to the animator when enabled. The animator
            // returns false only when it cannot start (window gone, rect unreadable) — then
            // we fall through to the instant path so the window is never left unplaced.
            if (_shouldAnimate?.Invoke() == true && _animator is not null)
            {
                bool began = _animator.Begin(placement.Hwnd, placement.TargetRect, placement.Kind, placement.BringToFront);
                if (began)
                {
                    succeeded++;
                    succeededHwnds.Add(placement.Hwnd);
                    _logger?.Info($"Placement animated: hwnd={placement.Hwnd}, slot={placement.SlotId}, kind={placement.Kind}, target={placement.TargetRect}, bringToFront={placement.BringToFront}");
                    return true;
                }

                _logger?.Info($"Animation declined for hwnd={placement.Hwnd}, falling back to instant placement");
            }

            if (placement.Kind == PlacementKind.Maximize)
            {
                _logger?.Info($"MaximizeWindow called: hwnd={placement.Hwnd}");
                var (maxResult, maxWin32Error) = _positioner.MaximizeWindow(placement.Hwnd);
                if (maxResult)
                {
                    succeeded++;
                    succeededHwnds.Add(placement.Hwnd);
                    _logger?.Info($"MaximizeWindow succeeded: hwnd={placement.Hwnd}");
                }
                else
                {
                    failed++;
                    var maxMsg = $"MaximizeWindow failed: hwnd={placement.Hwnd}, win32Error={maxWin32Error}";
                    errors.Add(maxMsg);
                    _logger?.Error(maxMsg);
                }
                return maxResult;
            }

            _logger?.Info($"SetWindowPos called: hwnd={placement.Hwnd}, bringToFront={placement.BringToFront}");
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var (result, win32Error) = _positioner.SetWindowPosition(placement.Hwnd, placement.TargetRect, placement.BringToFront);
            long elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000 / System.Diagnostics.Stopwatch.Frequency;

            if (result)
            {
                succeeded++;
                succeededHwnds.Add(placement.Hwnd);
                _logger?.Info($"SetWindowPos succeeded: hwnd={placement.Hwnd}, slot={placement.SlotId}, durationMs={elapsedMs}");
                if (elapsedMs >= SlowWin32CallThresholdMs)
                {
                    _tracker?.MarkUnstable(placement.Hwnd, now, $"slow-setwindowpos-{elapsedMs}ms");
                    _logger?.Warn($"Placement warning: SetWindowPos took {elapsedMs}ms, hwnd={placement.Hwnd}, slot={placement.SlotId}");
                }
            }
            else
            {
                failed++;
                _tracker?.MarkUnstable(placement.Hwnd, now, $"setwindowpos-failed-{win32Error}");
                var msg = $"SetWindowPos failed: hwnd={placement.Hwnd}, slot={placement.SlotId}, win32Error={win32Error}";
                errors.Add(msg);
                _logger?.Error(msg);
            }

            return result;
        }
        catch (Exception ex)
        {
            failed++;
            var msg = $"Exception for hwnd={placement.Hwnd}, slot={placement.SlotId}: {ex.Message}";
            errors.Add(msg);
            _logger?.Error($"Placement exception: hwnd={placement.Hwnd}, slot={placement.SlotId}", ex);
            return false;
        }
    }
}
