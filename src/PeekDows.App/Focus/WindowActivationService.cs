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

    /// <summary>
    /// How long a window that failed activation (or was detected as unresponsive) is kept on
    /// a cooldown so Directional Focus does not hammer it again. Chosen so a transient stall
    /// recovers quickly while a genuinely hung app is left alone for a few seconds.
    /// </summary>
    private const int FailedActivationCooldownMs = 3000;

    /// <summary>
    /// Upper bound on the responsiveness probe. A responsive window answers WM_NULL in well
    /// under 1ms; 200ms is generous for a busy-but-healthy app while still bounding how long
    /// PeekDows can wait on a frozen target.
    /// </summary>
    private const uint ResponsivenessProbeTimeoutMs = 200;

    private readonly Dictionary<IntPtr, DateTime> _failedCooldownUntil = new();
    private readonly Func<DateTime> _nowProvider;

    public WindowActivationService(IWindowActivationApi api, FileLogger? logger = null)
        : this(api, () => DateTime.Now, logger)
    {
    }

    /// <summary>
    /// Internal ctor that accepts a clock for deterministic cooldown tests.
    /// </summary>
    internal WindowActivationService(IWindowActivationApi api, Func<DateTime> nowProvider, FileLogger? logger = null)
    {
        _api = api;
        _nowProvider = nowProvider;
        _logger = logger;
    }

    /// <summary>
    /// True when <paramref name="hwnd"/> is currently on its post-failure cooldown and must
    /// not be re-activated yet. Pure lookup — no Win32 calls — so it is unit-testable.
    /// </summary>
    internal bool IsInCooldown(IntPtr hwnd, DateTime now)
    {
        return _failedCooldownUntil.TryGetValue(hwnd, out var until) && now < until;
    }

    private void MarkCooldown(IntPtr hwnd, DateTime now)
    {
        _failedCooldownUntil[hwnd] = now.AddMilliseconds(FailedActivationCooldownMs);
    }

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

        // Per-hwnd cooldown: a window that just failed activation (or was detected as hung)
        // is skipped entirely for a few seconds. This stops a stuck target from being
        // spammed on every gesture tick and keeps PeekDows usable while that app recovers.
        if (IsInCooldown(hwnd, now))
        {
            _logger?.Info($"Directional focus skipped: hwnd={hwnd} in failed activation cooldown");
            return false;
        }

        // Responsiveness probe BEFORE any blocking call. AttachThreadInput synchronises
        // message queues with the target thread; if that thread is hung, every subsequent
        // call (SetForegroundWindow, SetFocus, ...) would block PeekDows's UI thread until
        // the app unfreezes. The probe bounds that risk to ~ResponsivenessProbeTimeoutMs.
        if (!_api.IsResponsive(hwnd, ResponsivenessProbeTimeoutMs))
        {
            _logger?.Warn($"Directional focus skipped: target window not responding, hwnd={hwnd}");
            MarkCooldown(hwnd, now);
            return false;
        }

        var title = GetWindowTitle(hwnd);
        var pid = _api.GetWindowProcessId(hwnd);
        _logger?.Info($"Activation step: target hwnd={hwnd}, title={title}, pid={pid}");

        bool wasMinimized = _api.IsIconic(hwnd);
        if (wasMinimized)
        {
            bool restoreResult = _api.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            _logger?.Info($"Activation step: ShowWindow(SW_RESTORE)={restoreResult}");
        }
        else
        {
            _logger?.Info("Activation step: ShowWindow restore skipped/not needed");
        }

        var foregroundHwnd = _api.GetForegroundWindow();
        _logger?.Info($"Activation step: current foregroundHwnd={foregroundHwnd}");

        uint foregroundThreadId = foregroundHwnd != IntPtr.Zero
            ? _api.GetWindowThreadProcessId(foregroundHwnd, out _)
            : 0;
        uint targetThreadId = _api.GetWindowThreadProcessId(hwnd, out _);
        uint currentThreadId = _api.GetCurrentThreadId();

        _logger?.Info($"Activation step: foregroundThread={foregroundThreadId}, targetThread={targetThreadId}, currentThread={currentThreadId}");

        bool attachedForeground = false;
        bool attachedTarget = false;

        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attachedForeground = _api.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                _logger?.Info($"Activation step: AttachThreadInput(current→foreground)={attachedForeground}");
            }

            if (targetThreadId != 0 && targetThreadId != currentThreadId && targetThreadId != foregroundThreadId)
            {
                attachedTarget = _api.AttachThreadInput(currentThreadId, targetThreadId, true);
                _logger?.Info($"Activation step: AttachThreadInput(current→target)={attachedTarget}");
            }

            bool bringToTop = _api.BringWindowToTop(hwnd);
            _logger?.Info($"Activation step: BringWindowToTop={bringToTop}");

            uint setPosFlags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW;
            bool setPos = _api.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0, setPosFlags);
            _logger?.Info($"Activation step: SetWindowPos(HWND_TOP)={setPos}");

            bool setFg = _api.SetForegroundWindow(hwnd);
            _logger?.Info($"Activation step: SetForegroundWindow={setFg}");

            if (attachedForeground || attachedTarget)
            {
                var focusResult = _api.SetFocus(hwnd);
                _logger?.Info($"Activation step: SetFocus result={focusResult}");
            }

            var finalForeground = _api.GetForegroundWindow();
            bool success = finalForeground == hwnd;
            _logger?.Info($"Activation result: success={success}, foregroundHwnd={finalForeground}, expected={hwnd}");

            if (!success)
            {
                _logger?.Warn("Activation normal sequence failed, trying topmost fallback");
                bool setTopmost = _api.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, setPosFlags);
                _logger?.Info($"Activation step: SetWindowPos(HWND_TOPMOST)={setTopmost}");

                _api.SetForegroundWindow(hwnd);

                bool setNoTopmost = _api.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, setPosFlags);
                _logger?.Info($"Activation step: SetWindowPos(HWND_NOTOPMOST)={setNoTopmost}");

                finalForeground = _api.GetForegroundWindow();
                success = finalForeground == hwnd;
                _logger?.Info($"Activation fallback result: success={success}, foregroundHwnd={finalForeground}");
            }

            if (!success)
            {
                // Activation genuinely failed (not a hung window, but the window would not
                // come to the foreground). Put the hwnd on cooldown so Directional Focus
                // does not retry it on the next tick and pile up calls against a target that
                // is not cooperating.
                MarkCooldown(hwnd, _nowProvider());
                _logger?.Info($"Directional focus activation failed, hwnd cooldown started: hwnd={hwnd}, backoffMs={FailedActivationCooldownMs}");
            }

            return success;
        }
        finally
        {
            if (attachedForeground)
            {
                _api.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                _logger?.Info("Activation step: DetachThreadInput(current→foreground)");
            }
            if (attachedTarget)
            {
                _api.AttachThreadInput(currentThreadId, targetThreadId, false);
                _logger?.Info("Activation step: DetachThreadInput(current→target)");
            }
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
