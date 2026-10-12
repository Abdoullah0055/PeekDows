using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

/// <summary>
/// F1 — Undo Arrange. Bounded LIFO of pre-arrange snapshots (cap 3, oldest dropped).
/// Push captures {hwnd, rect, wasMaximized} per arrangeable window; Undo restores the
/// most recent snapshot best-effort: dead hwnds are skipped silently, each window is
/// wrapped in its own try/catch so one failure never blocks the others, and Undo itself
/// never throws.
/// Testability seam: the liveness probe and logger are optional ctor dependencies
/// (same pattern as DirectionalFocusRegistry); the positioner is injected per Undo
/// call, never stored. The <see cref="IWindowPositioner"/>-typed overload is internal
/// because that interface is internal to Core (a public signature would be CS0051);
/// production callers (PeekDows.App) use the parameterless public Undo().
/// </summary>
public sealed class ArrangeHistoryService
{
    private const int MaxSnapshots = 3;

    private readonly FileLogger? _logger;
    private readonly Func<IntPtr, bool> _isWindowValid;
    private readonly List<IReadOnlyList<SnapshotEntry>> _history = new();
    private readonly object _lock = new();

    public ArrangeHistoryService(FileLogger? logger = null, Func<IntPtr, bool>? isWindowValid = null)
    {
        _logger = logger;
        _isWindowValid = isWindowValid ?? (hwnd => NativeMethods.IsWindow(hwnd));
    }

    public bool CanUndo
    {
        get
        {
            lock (_lock)
            {
                return _history.Count > 0;
            }
        }
    }

    /// <summary>
    /// Captures a snapshot of the windows about to be arranged. Call BEFORE applying
    /// placements. Null/empty/Zero-hwnd inputs are ignored (logged, never thrown).
    /// </summary>
    public void Push(IReadOnlyList<ManagedWindow>? arrangeable)
    {
        try
        {
            if (arrangeable == null || arrangeable.Count == 0)
            {
                _logger?.Info("ArrangeHistory.Push skipped: no arrangeable windows");
                return;
            }

            var snapshot = new List<SnapshotEntry>(arrangeable.Count);
            foreach (var w in arrangeable)
            {
                try
                {
                    if (w is null || w.Hwnd == IntPtr.Zero)
                        continue;
                    // Rect is a value type: captured by copy, later mutations of the
                    // ManagedWindow cannot corrupt the snapshot.
                    snapshot.Add(new SnapshotEntry(w.Hwnd, w.CurrentRect, w.IsMaximized));
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"ArrangeHistory.Push capture failed for one window: {ex.Message}");
                }
            }

            if (snapshot.Count == 0)
            {
                _logger?.Info("ArrangeHistory.Push skipped: snapshot empty after capture");
                return;
            }

            lock (_lock)
            {
                _history.Add(snapshot);
                while (_history.Count > MaxSnapshots)
                    _history.RemoveAt(0); // drop oldest
            }

            _logger?.Info($"ArrangeHistory.Push: captured {snapshot.Count} window(s), depth={_history.Count}");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"ArrangeHistory.Push failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Production entry point: restores the most recent snapshot using the default
    /// Win32 positioner. Never throws.
    /// </summary>
    public void Undo()
    {
        try
        {
            Undo(new Win32WindowPositioner());
        }
        catch (Exception ex)
        {
            _logger?.Warn($"ArrangeHistory.Undo failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores the most recent snapshot (LIFO — each Undo consumes one level, so up
    /// to 3 consecutive undos walk back 3 arranges). Per window: skip dead hwnds
    /// silently, re-pose at the captured rect FIRST, then re-maximize when the window
    /// was maximized at capture time. Never throws.
    /// </summary>
    internal void Undo(IWindowPositioner positioner)
    {
        IReadOnlyList<SnapshotEntry>? snapshot;
        try
        {
            if (positioner is null)
            {
                _logger?.Warn("ArrangeHistory.Undo skipped: null positioner");
                return;
            }

            lock (_lock)
            {
                if (_history.Count == 0)
                {
                    _logger?.Info("ArrangeHistory.Undo skipped: history empty");
                    return;
                }
                snapshot = _history[_history.Count - 1];
                _history.RemoveAt(_history.Count - 1);
            }

            _logger?.Info($"ArrangeHistory.Undo: restoring {snapshot.Count} window(s), remaining depth={_history.Count}");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"ArrangeHistory.Undo dequeue failed: {ex.Message}");
            return;
        }

        foreach (var entry in snapshot)
        {
            try
            {
                bool alive;
                try
                {
                    alive = _isWindowValid(entry.Hwnd);
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"ArrangeHistory.Undo liveness probe failed for hwnd={entry.Hwnd}: {ex.Message}");
                    continue;
                }

                if (!alive)
                {
                    _logger?.Info($"ArrangeHistory.Undo skipped dead window: hwnd={entry.Hwnd}");
                    continue;
                }

                // Re-pose FIRST at the captured rect, then re-maximize when needed —
                // a maximize issued before the move would be overwritten by the move.
                positioner.SetWindowPosition(entry.Hwnd, entry.Rect);
                if (entry.WasMaximized)
                    positioner.MaximizeWindow(entry.Hwnd);

                _logger?.Info($"ArrangeHistory.Undo restored: hwnd={entry.Hwnd}, rect={entry.Rect}, wasMaximized={entry.WasMaximized}");
            }
            catch (Exception ex)
            {
                _logger?.Warn($"ArrangeHistory.Undo failed for hwnd={entry.Hwnd}: {ex.Message}");
            }
        }
    }

    private readonly record struct SnapshotEntry(IntPtr Hwnd, Rect Rect, bool WasMaximized);
}
