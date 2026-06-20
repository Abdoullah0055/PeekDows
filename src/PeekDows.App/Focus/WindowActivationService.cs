using System;
using System.Runtime.InteropServices;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

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
}

public sealed class Win32ActivationApi : IWindowActivationApi
{
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
}

public sealed class WindowActivationService
{
    private readonly IWindowActivationApi _api;
    private readonly FileLogger? _logger;
    private readonly UnstableWindowTracker? _tracker;

    /// <summary>
    /// Upper bound on the responsiveness probe. A responsive window answers WM_NULL in well
    /// under 1ms; 200ms is generous for a busy-but-healthy app while still bounding how long
    /// PeekDows can wait on a frozen target.
    /// </summary>
    private const uint ResponsivenessProbeTimeoutMs = 200;

    private readonly Dictionary<IntPtr, DateTime> _failedCooldownUntil = new();
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

    private void MarkCooldown(IntPtr hwnd, DateTime now, string? reason = null)
    {
        _failedCooldownUntil[hwnd] = now.AddMilliseconds(3000);
        _tracker?.MarkUnstable(hwnd, now, reason ?? "activation-failed");
    }

    /// <summary>
    /// Minimal, defensive activation path. Per the hardening spec, the normal path MUST NOT
    /// call <c>AttachThreadInput</c>, <c>SetFocus</c>, or any topmost fallback — all of those
    /// synchronise with / re-issue calls against the target thread and can freeze PeekDows's
    /// UI thread if the target is hung. On failure we back off (cooldown) rather than
    /// escalate.
    /// </summary>
    public bool Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            _logger?.Warn("Activation skipped: hwnd is zero");
            return false;
        }

        if (!_api.IsWindow(hwnd))
        {
            _logger?.Warn($"Activation skipped: invalid window handle hwnd={hwnd}");
            return false;
        }

        var now = _nowProvider();

        // Per-hwnd cooldown: a window that just failed activation (or was detected as hung
        // by the placement path) is skipped entirely for a few seconds.
        if (IsInCooldown(hwnd, now))
        {
            _logger?.Info($"Directional focus skipped: hwnd={hwnd} in failed activation cooldown");
            return false;
        }

        // Shared unstable tracker: a window marked unstable by WindowPlacementService (or any
        // other path) is off-limits for the full cooldown even after this service's shorter
        // 3s dict entry expires. No probe, no restore, no SetWindowPos, no SetForegroundWindow.
        if (_tracker is not null && _tracker.IsUnstable(hwnd, now))
        {
            _tracker.TryGetReason(hwnd, out var reason);
            _logger?.Info($"Directional focus skipped: hwnd in unstable cooldown, hwnd={hwnd}, reason={reason ?? "unknown"}");
            return false;
        }

        // Responsiveness probe BEFORE any blocking call. This is the only line that can wait
        // on the target thread, and it is bounded by ResponsivenessProbeTimeoutMs.
        if (!_api.IsResponsive(hwnd, ResponsivenessProbeTimeoutMs))
        {
            _logger?.Warn($"Directional focus skipped: target window not responding, hwnd={hwnd}");
            MarkCooldown(hwnd, now, "not-responding-activation");
            return false;
        }

        var title = GetWindowTitle(hwnd);
        var pid = _api.GetWindowProcessId(hwnd);
        _logger?.Info($"Activation step: target hwnd={hwnd}, title={title}, pid={pid}");

        // Restore minimised windows using the NON-BLOCKING variant. ShowWindow would wait for
        // the target thread to process the restore; ShowWindowAsync just posts it.
        if (_api.IsIconic(hwnd))
        {
            bool restoreResult = _api.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
            _logger?.Info($"Activation step: ShowWindowAsync(SW_RESTORE)={restoreResult}");
        }
        else
        {
            _logger?.Info("Activation step: restore skipped/not needed");
        }

        // Raise the window with a single async SetWindowPos(HWND_TOP). BringWindowToTop was
        // removed: it re-issues against the target thread and is redundant with the HWND_TOP
        // move that immediately follows.
        uint setPosFlags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_ASYNCWINDOWPOS;
        bool setPos = _api.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0, setPosFlags);
        _logger?.Info($"Activation step: SetWindowPos(HWND_TOP)={setPos}");

        bool setFg = _api.SetForegroundWindow(hwnd);
        _logger?.Info($"Activation step: SetForegroundWindow={setFg}");

        // Single foreground check. If it did not stick, we do NOT escalate (no topmost dance,
        // no second SetForegroundWindow) — the target is treated as unsafe and backed off.
        var finalForeground = _api.GetForegroundWindow();
        bool success = finalForeground == hwnd;
        _logger?.Info($"Activation result: success={success}, foregroundHwnd={finalForeground}, expected={hwnd}");

        if (!success)
        {
            MarkCooldown(hwnd, _nowProvider(), "foreground-mismatch");
            _logger?.Info($"Directional focus activation failed, hwnd cooldown started: hwnd={hwnd}");
        }

        return success;
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
