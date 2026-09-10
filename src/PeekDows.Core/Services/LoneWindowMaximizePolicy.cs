using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Per-monitor state machine behind the "lone window → native maximize" rule, with the
/// respect-user semantics:
/// - A monitor with exactly one arrangeable window gets a Maximize placement…
/// - …unless the user manually restored a window PeekDows had maximized on that monitor
///   (userRestored). While set, the lone window is left completely alone (no maximize,
///   no reposition) until the arrangeable window count on that monitor changes.
/// - Windows PeekDows itself maximized are tracked so they stay eligible for
///   RestoreAndReposition even when AllowRepositionMaximizedWindows is false.
/// Not thread-safe: called only from the UI-thread arrange path.
/// </summary>
public sealed class LoneWindowMaximizePolicy
{
    // B2 fix: key by stable monitor handle, not by Rect WorkArea. A 1px WorkArea change
    // (taskbar auto-hide, DPI scale switch, dock) previously orphaned the flag.
    // We keep a handle→Rect map and normalize Rect to 2px to tolerate jitter when
    // the handle is unknown (IntPtr.Zero fallback).
    private readonly Dictionary<IntPtr, bool> _userRestoredByHandle = new();
    private readonly Dictionary<IntPtr, Rect> _workAreaByHandle = new();
    private readonly Dictionary<IntPtr, int> _lastCountByHandle = new();
    private readonly HashSet<IntPtr> _peekDowsMaximized = new();
    private readonly FileLogger? _logger;

    // Legacy Rect-keyed state kept for backward-compat with callers/tests that still
    // pass Rect only (e.g. unit tests). Migrated on first handle-aware call.
    private readonly Dictionary<Rect, bool> _userRestoredByRect = new();
    private readonly Dictionary<Rect, int> _lastCountByRect = new();

    public LoneWindowMaximizePolicy() { }

    public LoneWindowMaximizePolicy(FileLogger? logger)
    {
        _logger = logger;
    }

    private static Rect Normalize(Rect r) => new(r.Left & ~1, r.Top & ~1, r.Width & ~1, r.Height & ~1);

    // Existing API — Rect-only (tests, legacy callers). Delegates to handle-aware with IntPtr.Zero.
    public void OnArrangeStarting(IReadOnlyDictionary<Rect, int> arrangeableCountByMonitor)
    {
        var byHandle = new Dictionary<IntPtr, (Rect rect, int count)>();
        foreach (var kv in arrangeableCountByMonitor)
            byHandle[kv.Key.GetHashCode()] = (kv.Key, kv.Value); // pseudo-handle when no real handle
        // For Rect-only callers we keep Rect-keyed logic but with normalization.
        var norm = arrangeableCountByMonitor.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value);
        foreach (var monitor in _userRestoredByRect.Keys.ToList())
        {
            bool stillExists = norm.TryGetValue(Normalize(monitor), out var count)
                && _lastCountByRect.TryGetValue(Normalize(monitor), out var last)
                && count == last;
            if (!stillExists)
            {
                _userRestoredByRect.Remove(monitor);
                _logger?.Info($"LoneWindowMaximizePolicy: userRestored reset for monitor={monitor} (count changed or monitor gone)");
            }
        }
        _lastCountByRect.Clear();
        foreach (var kv in norm)
            _lastCountByRect[kv.Key] = kv.Value;
    }

    /// <summary>Handle-aware overload — preferred in production (MultiMonitorLayoutService).</summary>
    public void OnArrangeStarting(IReadOnlyDictionary<IntPtr, (Rect workArea, int count)> arrangeableCountByHandle)
    {
        foreach (var h in _userRestoredByHandle.Keys.ToList())
        {
            bool stillExists = arrangeableCountByHandle.TryGetValue(h, out var cur)
                && _lastCountByHandle.TryGetValue(h, out var last)
                && cur.count == last;
            if (!stillExists)
            {
                _userRestoredByHandle.Remove(h);
                _workAreaByHandle.Remove(h);
                _logger?.Info($"LoneWindowMaximizePolicy: userRestored reset for monitor handle={h} (count changed or monitor gone)");
            }
        }
        _lastCountByHandle.Clear();
        foreach (var kv in arrangeableCountByHandle)
        {
            _lastCountByHandle[kv.Key] = kv.Value.count;
            _workAreaByHandle[kv.Key] = Normalize(kv.Value.workArea);
        }
    }

    /// <summary>True when the single arrangeable window on this monitor should be maximized.</summary>
    public bool ShouldMaximizeLoneWindow(Rect monitorWorkArea)
    {
        var n = Normalize(monitorWorkArea);
        return !_userRestoredByRect.TryGetValue(n, out var restored) || !restored;
    }

    public bool ShouldMaximizeLoneWindow(IntPtr monitorHandle, Rect monitorWorkArea)
    {
        if (monitorHandle != IntPtr.Zero)
        {
            if (_userRestoredByHandle.TryGetValue(monitorHandle, out var r)) return !r;
            // Fallback to rect when handle not yet seen (e.g. tests that never called handle overload)
        }
        return ShouldMaximizeLoneWindow(monitorWorkArea);
    }

    public void NotifyMaximizedByPeekDows(IntPtr hwnd)
    {
        _peekDowsMaximized.Add(hwnd);
    }

    /// <summary>PeekDows itself restored the window (RestoreAndReposition applied).</summary>
    public void NotifyRestoredByPeekDows(IntPtr hwnd)
    {
        _peekDowsMaximized.Remove(hwnd);
    }

    /// <summary>
    /// The user manually restored a window PeekDows had maximized. Suppresses lone-window
    /// maximize on that monitor until its window count changes.
    /// </summary>
    public void NotifyUserRestored(IntPtr hwnd, Rect monitorWorkArea)
    {
        _peekDowsMaximized.Remove(hwnd);
        var n = Normalize(monitorWorkArea);
        _userRestoredByRect[n] = true;
        _logger?.Info($"LoneWindowMaximizePolicy: user restored window hwnd={hwnd} on monitor={monitorWorkArea}, lone-window maximize suppressed");
    }

    public void NotifyUserRestored(IntPtr hwnd, IntPtr monitorHandle, Rect monitorWorkArea)
    {
        _peekDowsMaximized.Remove(hwnd);
        if (monitorHandle != IntPtr.Zero)
        {
            _userRestoredByHandle[monitorHandle] = true;
            _workAreaByHandle[monitorHandle] = Normalize(monitorWorkArea);
            _logger?.Info($"LoneWindowMaximizePolicy: user restored window hwnd={hwnd} on monitor handle={monitorHandle}, lone-window maximize suppressed");
        }
        else
        {
            NotifyUserRestored(hwnd, monitorWorkArea);
        }
    }

    public bool WasMaximizedByPeekDows(IntPtr hwnd) => _peekDowsMaximized.Contains(hwnd);

    /// <summary>Drops tracked hwnds that are no longer known windows (closed / HWND reuse guard).</summary>
    public void PruneMaximizedSet(IReadOnlyCollection<IntPtr> knownHwnds)
    {
        var known = new HashSet<IntPtr>(knownHwnds);
        _peekDowsMaximized.RemoveWhere(h => !known.Contains(h));
    }
}
