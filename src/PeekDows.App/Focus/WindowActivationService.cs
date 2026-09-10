using System;
using System.Runtime.InteropServices;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

/// <summary>
/// Outcome of an activation attempt. More expressive than a plain bool so the caller
/// (<see cref="DirectionalFocusService"/>) can treat "foreground not yet confirmed" (Pending)
/// differently from a hard denial — Pending must not log a failure or apply a negative
/// cooldown, because a delayed recheck often confirms success shortly after.
/// </summary>
public enum ActivationStatus
{
    /// <summary>Foreground became the target window synchronously.</summary>
    Success,
    /// <summary>Activation was issued but foreground is not confirmed yet; a delayed recheck is scheduled.</summary>
    Pending,
    /// <summary>The foreground lock denied activation (taskbar flash likely). Short cooldown only.</summary>
    ForegroundDenied,
    /// <summary>Skipped because the window is on the unstable tracker (hung/slow/recent failure).</summary>
    UnstableSkipped,
    /// <summary>The target window did not answer the responsiveness probe (true hung → tracker).</summary>
    NotResponding
}

public interface IWindowActivationApi
{
    bool IsWindow(IntPtr hwnd);
    bool IsIconic(IntPtr hwnd);
    bool ShowWindow(IntPtr hwnd, int cmdShow);
    /// <summary>
    /// Non-blocking variant of <see cref="ShowWindow"/>: posts the command without waiting
    /// for the target thread. Used to restore minimised windows so a hung target cannot
    /// block activation.
    /// </summary>
    bool ShowWindowAsync(IntPtr hwnd, int cmdShow);
    IntPtr GetForegroundWindow();
    uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    uint GetCurrentThreadId();
    bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    bool BringWindowToTop(IntPtr hwnd);
    bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);
    bool SetForegroundWindow(IntPtr hwnd);
    IntPtr SetFocus(IntPtr hwnd);
    int GetWindowText(IntPtr hwnd, System.Text.StringBuilder sb, int maxCount);
    uint GetWindowProcessId(IntPtr hwnd);

    /// <summary>
    /// Probes whether the window's owning thread is currently pumping its message queue
    /// (i.e. the window is responsive) within the given timeout. Returns <c>false</c> if the
    /// window is hung/unresponsive. Implementations must use a bounded, non-blocking probe
    /// such as <c>SendMessageTimeout(WM_NULL, SMTO_ABORTIFHUNG)</c> so a frozen target can
    /// never block the caller.
    /// </summary>
    bool IsResponsive(IntPtr hwnd, uint timeoutMs);

    /// <summary>
    /// Sends a benign ALT key-down/key-up pulse via <c>SendInput</c>. Windows grants
    /// <c>SetForegroundWindow</c> to a thread that has recently synthesised input, so this
    /// "unlocks" the foreground lock that otherwise makes the target window only flash orange
    /// in the taskbar instead of coming to the front. Safe and non-blocking: no
    /// <c>AttachThreadInput</c>, no <c>SetFocus</c>, no topmost fallback. Returns true if the
    /// pulse was delivered.
    /// </summary>
    bool TryUnlockForegroundWithAltPulse();
}

public sealed class Win32ActivationApi : IWindowActivationApi
{
    private readonly IInputSimulator _inputSimulator;

    public Win32ActivationApi() : this(new Win32InputSimulator()) { }

    public Win32ActivationApi(IInputSimulator inputSimulator)
    {
        _inputSimulator = inputSimulator;
    }

    public bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);
    public bool IsIconic(IntPtr hwnd) => NativeMethods.IsIconic(hwnd);
    public bool ShowWindow(IntPtr hwnd, int cmdShow) => NativeMethods.ShowWindow(hwnd, cmdShow);
    public bool ShowWindowAsync(IntPtr hwnd, int cmdShow) => NativeMethods.ShowWindowAsync(hwnd, cmdShow);
    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();
    public uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid) => NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
    public uint GetCurrentThreadId() => NativeMethods.GetCurrentThreadId();
    public bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach) => NativeMethods.AttachThreadInput(idAttach, idAttachTo, fAttach);
    public bool BringWindowToTop(IntPtr hwnd) => NativeMethods.BringWindowToTop(hwnd);
    public bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags) => NativeMethods.SetWindowPos(hwnd, hwndInsertAfter, x, y, cx, cy, flags);
    public bool SetForegroundWindow(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);
    public IntPtr SetFocus(IntPtr hwnd) => NativeMethods.SetFocus(hwnd);
    public int GetWindowText(IntPtr hwnd, System.Text.StringBuilder sb, int maxCount) => NativeMethods.GetWindowText(hwnd, sb, maxCount);
    public uint GetWindowProcessId(IntPtr hwnd) { NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid); return pid; }

    public bool IsResponsive(IntPtr hwnd, uint timeoutMs)
    {
        // SMTO_ABORTIFHUNG returns immediately (as failure) if the target thread is not
        // pumping messages — the canonical Win32 way to detect a hung window without
        // blocking. We must NOT use SMTO_BLOCK here: it would stop PeekDows's own UI thread
        // from pumping its queue, recreating the freeze we are trying to prevent.
        IntPtr result = NativeMethods.SendMessageTimeout(
            hwnd,
            NativeMethods.WM_NULL,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG,
            timeoutMs,
            out _);

        // A non-zero return means the message was delivered within the timeout.
        return result != IntPtr.Zero;
    }

    public bool TryUnlockForegroundWithAltPulse()
    {
        // Delegated to the input simulator so the real SendInput/keybd_event plumbing (and
        // its logging) lives in one testable place. Non-blocking, no AttachThreadInput/SetFocus.
        return _inputSimulator.TryAltPulse();
    }
}

public sealed class WindowActivationService : IDisposable
{
    private readonly IWindowActivationApi _api;
    private readonly FileLogger? _logger;
    private readonly UnstableWindowTracker? _tracker;
    private readonly List<System.Windows.Forms.Timer> _pendingRecheckTimers = new();
    private bool _disposed;

    /// <summary>
    /// Upper bound on the responsiveness probe. A responsive window answers WM_NULL in well
    /// under 1ms; 200ms is generous for a busy-but-healthy app while still bounding how long
    /// PeekDows can wait on a frozen target.
    /// </summary>
    private const uint ResponsivenessProbeTimeoutMs = 200;

    /// <summary>Short throttle after a successful activation, pacing rapid slot-hopping.</summary>
    private const int NormalActivationCooldownMs = 150;

    /// <summary>
    /// Short cooldown after a foreground-mismatch — NOT a dangerous signal, so never reaches
    /// the 15s tracker. Bounds rapid retries without excluding healthy windows from arrange.
    /// </summary>
    private const int ForegroundMismatchCooldownMs = 400;

    /// <summary>
    /// Local cooldown applied alongside the tracker for genuinely dangerous activation
    /// failures (not-responding). The tracker's 15s is the dominant protection; this just
    /// guards the dict within the same service.
    /// </summary>
    private const int HardFailureCooldownMs = 3000;

    /// <summary>Delay before the deferred, non-blocking foreground recheck (C4: was 100ms too short on slow machines -> 200ms).</summary>
    private const int ForegroundRecheckDelayMs = 200;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, DateTime> _failedCooldownUntil = new();
    private readonly Func<DateTime> _nowProvider;

    public WindowActivationService(IWindowActivationApi api, FileLogger? logger = null)
        : this(api, () => DateTime.Now, logger, null)
    {
    }

    public WindowActivationService(IWindowActivationApi api, FileLogger? logger, UnstableWindowTracker? tracker)
        : this(api, () => DateTime.Now, logger, tracker)
    {
    }

    /// <summary>
    /// Internal ctor that accepts a clock for deterministic cooldown tests.
    /// </summary>
    internal WindowActivationService(IWindowActivationApi api, Func<DateTime> nowProvider, FileLogger? logger = null, UnstableWindowTracker? tracker = null)
    {
        _api = api;
        _nowProvider = nowProvider;
        _logger = logger;
        _tracker = tracker;
    }

    /// <summary>
    /// True when <paramref name="hwnd"/> is currently on its post-failure cooldown and must
    /// not be re-activated yet. Pure lookup — no Win32 calls — so it is unit-testable.
    /// </summary>
    internal bool IsInCooldown(IntPtr hwnd, DateTime now)
    {
        return _failedCooldownUntil.TryGetValue(hwnd, out var until) && now < until;
    }

    private void MarkCooldown(IntPtr hwnd, DateTime now, string? reason = null, bool markUnstable = true)
    {
        // foreground-mismatch is NOT a dangerous Win32 signal — Windows routinely denies
        // SetForegroundWindow when PeekDows does not already own the foreground. Treating it
        // like a hung window (15s unstable) excludes healthy windows from arrange and focus
        // for far too long, which is what made the shortcut feel sluggish. So a mismatch gets
        // only a SHORT local cooldown; only genuinely dangerous reasons reach the tracker.
        int cooldownMs = reason == "foreground-mismatch" ? ForegroundMismatchCooldownMs : HardFailureCooldownMs;
        _failedCooldownUntil[hwnd] = now.AddMilliseconds(cooldownMs);
        if (markUnstable)
        {
            _tracker?.MarkUnstable(hwnd, now, reason ?? "activation-failed");
        }
    }

    /// <summary>
    /// Minimal, defensive activation path returning an expressive <see cref="ActivationStatus"/>.
    /// Per the hardening spec, the normal path MUST NOT call <c>AttachThreadInput</c>,
    /// <c>SetFocus</c>, or any topmost fallback — all of those synchronise with / re-issue
    /// calls against the target thread and can freeze PeekDows's UI thread if the target is
    /// hung. Foreground lock is defeated instead with a benign ALT pulse via SendInput.
    /// </summary>
    public ActivationStatus Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            _logger?.Warn("Activation skipped: hwnd is zero");
            return ActivationStatus.ForegroundDenied;
        }

        if (!_api.IsWindow(hwnd))
        {
            _logger?.Warn($"Activation skipped: invalid window handle hwnd={hwnd}");
            return ActivationStatus.ForegroundDenied;
        }

        var now = _nowProvider();

        // Shared unstable tracker FIRST: a window marked unstable by WindowPlacementService
        // (not-responding, slow SetWindowPos, setwindowpos-failed) is off-limits for the full
        // 15s cooldown. A foreground-mismatch never reaches the tracker, so healthy windows
        // are never blocked here for 15s.
        if (_tracker is not null && _tracker.IsUnstable(hwnd, now))
        {
            _tracker.TryGetReason(hwnd, out var reason);
            _logger?.Info($"Directional focus skipped: hwnd in unstable cooldown, hwnd={hwnd}, reason={reason ?? "unknown"}");
            return ActivationStatus.UnstableSkipped;
        }

        // Short per-hwnd cooldown for recent activation failures (including foreground
        // mismatch). Bounds rapid retries without the 15s penalty of the tracker.
        if (IsInCooldown(hwnd, now))
        {
            _logger?.Info($"Directional focus skipped: hwnd={hwnd} in failed activation cooldown");
            return ActivationStatus.UnstableSkipped;
        }

        // Responsiveness probe BEFORE any blocking call. Bounded by ResponsivenessProbeTimeoutMs.
        // A window that fails here is a TRUE dangerous signal → tracker (15s).
        if (!_api.IsResponsive(hwnd, ResponsivenessProbeTimeoutMs))
        {
            _logger?.Warn($"Directional focus skipped: target window not responding, hwnd={hwnd}");
            MarkCooldown(hwnd, now, "not-responding-activation", markUnstable: true);
            return ActivationStatus.NotResponding;
        }

        var title = GetWindowTitle(hwnd);
        var pid = _api.GetWindowProcessId(hwnd);
        _logger?.Info($"Activation step: target hwnd={hwnd}, title={title}, pid={pid}");

        // Restore minimised windows using the NON-BLOCKING variant.
        if (_api.IsIconic(hwnd))
        {
            bool restoreResult = _api.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
            _logger?.Info($"Activation step: ShowWindowAsync(SW_RESTORE)={restoreResult}");
        }
        else
        {
            _logger?.Info("Activation step: restore skipped/not needed");
        }

        // Foreground unlock: send a benign ALT pulse so Windows' foreground-lock heuristic
        // considers PeekDows eligible to grant SetForegroundWindow. This is the safe
        // alternative to the old AttachThreadInput trick — it cannot block PeekDows's UI
        // thread or synchronise queues with a hung target.
        _logger?.Info("Activation foreground unlock: sending ALT pulse");
        bool altPulse = _api.TryUnlockForegroundWithAltPulse();
        _logger?.Info($"Activation foreground unlock: ALT pulse delivered={altPulse}");

        // Request foreground, then raise the window to the top. Requesting foreground BEFORE
        // the HWND_TOP move empirically yields the best result: once foreground is granted,
        // the subsequent SetWindowPos(HWND_TOP) visually raises the window above its peers.
        // SWP_ASYNCWINDOWPOS keeps the move non-blocking.
        bool setFg = _api.SetForegroundWindow(hwnd);
        _logger?.Info($"Activation foreground unlock: SetForegroundWindow result={setFg}");

        uint setPosFlags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_ASYNCWINDOWPOS;
        bool setPos = _api.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0, setPosFlags);
        _logger?.Info($"Activation step: SetWindowPos(HWND_TOP)={setPos}");

        // Immediate foreground check. If it already stuck → Success. Otherwise the activation
        // is Pending: Windows often settles the foreground a few ms later (the "orange flash
        // but window not on top" symptom). We schedule a delayed recheck; Pending is NOT a
        // failure from the caller's perspective.
        var finalForeground = _api.GetForegroundWindow();
        bool immediateSuccess = finalForeground == hwnd;
        _logger?.Info($"Activation result: immediateSuccess={immediateSuccess}, foregroundHwnd={finalForeground}, expected={hwnd}");

        if (immediateSuccess)
        {
            // Healthy activation: short throttle so the next slot-hop is paced.
            _failedCooldownUntil[hwnd] = now.AddMilliseconds(NormalActivationCooldownMs);
            return ActivationStatus.Success;
        }

        // Not yet confirmed → Pending. Apply the short pacing cooldown and schedule a
        // deferred, non-blocking recheck. No tracker, no hard failure.
        _failedCooldownUntil[hwnd] = now.AddMilliseconds(ForegroundMismatchCooldownMs);
        _logger?.Info($"Activation pending: delayed recheck scheduled, hwnd={hwnd}");
        ScheduleForegroundRecheck(hwnd);
        return ActivationStatus.Pending;
    }

    /// <summary>
    /// Deferred, non-blocking re-attempt: Windows sometimes grants foreground only a few
    /// milliseconds after SetForegroundWindow (the "taskbar flashes orange but window stays
    /// behind" symptom). We retry once after a short delay WITHOUT blocking the UI thread:
    /// ALT pulse + SetForegroundWindow + SetWindowPos(HWND_TOP). No AttachThreadInput /
    /// SetFocus / topmost. On success we clear the short cooldown; on failure we log that the
    /// foreground was denied (the short cooldown already applies from the synchronous path).
    /// </summary>
    private void ScheduleForegroundRecheck(IntPtr hwnd)
    {
        if (_disposed) return;
        var timer = new System.Windows.Forms.Timer { Interval = ForegroundRecheckDelayMs };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Tick -= handler!;
            timer.Stop();
            timer.Dispose();
            lock (_pendingRecheckTimers) _pendingRecheckTimers.Remove(timer);
            try
            {
                if (_disposed || !_api.IsWindow(hwnd)) return;
                _api.TryUnlockForegroundWithAltPulse();
                _api.SetForegroundWindow(hwnd);
                uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_ASYNCWINDOWPOS;
                _api.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0, flags);
                bool ok = _api.GetForegroundWindow() == hwnd;
                _logger?.Info($"Activation delayed recheck: success={ok}, foregroundHwnd={_api.GetForegroundWindow()}, expected={hwnd}");
                if (ok) _failedCooldownUntil.TryRemove(hwnd, out _);
                else _logger?.Info($"Activation foreground denied: taskbar flash likely, hwnd={hwnd}");
            }
            catch (Exception ex) { _logger?.Warn($"Activation deferred recheck failed: hwnd={hwnd}, {ex.Message}"); }
        };
        timer.Tick += handler;
        lock (_pendingRecheckTimers) _pendingRecheckTimers.Add(timer);
        timer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_pendingRecheckTimers)
        {
            foreach (var t in _pendingRecheckTimers)
            {
                try { t.Stop(); t.Dispose(); } catch { }
            }
            _pendingRecheckTimers.Clear();
        }
    }

    private string GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            _api.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return "<unknown>";
        }
    }
}
