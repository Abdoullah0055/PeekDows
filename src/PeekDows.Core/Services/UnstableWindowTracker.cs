using System;
using System.Collections.Generic;

namespace PeekDows.Core.Services;

/// <summary>
/// Defensive cache of windows that have recently misbehaved during arrange/activation:
/// hung, slow to respond, blocking SetWindowPos, activation failures, or whose monitor
/// could not be resolved. Arrange and Directional Focus consult it to skip such windows
/// instead of risking another blocking Win32 call on the UI thread.
/// </summary>
/// <remarks>
/// Every entry carries an expiry timestamp. Entries are not eagerly removed; they simply
/// stop being reported once expired. Lookups are O(1).
/// </remarks>
public sealed class UnstableWindowTracker
{
    /// <summary>
    /// How long a window stays on the unstable list. A transient stall (a busy GC, a slow
    /// disk) recovers within a few seconds; a genuinely hung app would otherwise be retried
    /// on every gesture tick, so we keep it off-limits for a comfortable window.
    /// </summary>
    private const int UnstableWindowCooldownMs = 15000;

    private readonly Dictionary<IntPtr, DateTime> _unstableUntil = new();
    private readonly Dictionary<IntPtr, string> _reasons = new();
    private readonly Func<DateTime> _nowProvider;
    private readonly FileLogger? _logger;

    public UnstableWindowTracker() : this(() => DateTime.Now, null) { }

    public UnstableWindowTracker(FileLogger? logger) : this(() => DateTime.Now, logger) { }

    /// <summary>Internal ctor that accepts a clock for deterministic cooldown tests.</summary>
    internal UnstableWindowTracker(Func<DateTime> nowProvider, FileLogger? logger = null)
    {
        _nowProvider = nowProvider;
        _logger = logger;
    }

    /// <summary>
    /// True when <paramref name="hwnd"/> is currently considered unstable and must be
    /// skipped by arrange/activation. Pure lookup — no Win32 calls — so it is unit-testable.
    /// </summary>
    public bool IsUnstable(IntPtr hwnd, DateTime? now = null)
    {
        var t = now ?? _nowProvider();
        return _unstableUntil.TryGetValue(hwnd, out var until) && t < until;
    }

    /// <summary>Records the human-readable reason a window is unstable (for logging/debug).</summary>
    public bool TryGetReason(IntPtr hwnd, out string? reason)
    {
        if (_unstableUntil.TryGetValue(hwnd, out var until) && _nowProvider() < until)
        {
            reason = _reasons.TryGetValue(hwnd, out var r) ? r : null;
            return true;
        }
        reason = null;
        return false;
    }

    /// <summary>
    /// Marks <paramref name="hwnd"/> as unstable for <see cref="UnstableWindowCooldownMs"/>.
    /// </summary>
    public void MarkUnstable(IntPtr hwnd, DateTime? now = null, string? reason = null)
    {
        var t = now ?? _nowProvider();
        _unstableUntil[hwnd] = t.AddMilliseconds(UnstableWindowCooldownMs);
        if (reason != null) _reasons[hwnd] = reason;
        _logger?.Warn($"Window marked unstable: hwnd={hwnd}, reason={reason ?? "unknown"}, cooldownMs={UnstableWindowCooldownMs}");
    }

    /// <summary>
    /// Circuit-breaker helper: true if ANY window was marked unstable within the last
    /// <paramref name="withinMs"/>. Used by ArrangeNow to back off right after a Directional
    /// Focus activation failure instead of immediately re-arranging the same windows.
    /// </summary>
    public bool HasRecentUnstable(DateTime? now = null, int withinMs = 1000)
    {
        var t = now ?? _nowProvider();
        var cutoff = t.AddMilliseconds(-withinMs);
        foreach (var kv in _unstableUntil)
        {
            // The mark timestamp is (expiry - cooldown). A window marked within the window
            // (i.e. its expiry is within cooldown of now-cutoff) counts as recent.
            var markedAt = kv.Value.AddMilliseconds(-UnstableWindowCooldownMs);
            if (markedAt >= cutoff && t < kv.Value)
                return true;
        }
        return false;
    }

    /// <summary>Test/debug helper: clears all unstable state.</summary>
    internal void Clear()
    {
        _unstableUntil.Clear();
        _reasons.Clear();
    }
}
