using System;
using System.Runtime.InteropServices;
using System.Threading;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

/// <summary>
/// WH_KEYBOARD_LL guard that hides the Ctrl+Shift chord completion from Windows
/// while a Directional Focus gesture may be in progress, so the OS never flips
/// the keyboard layout (FR↔EN). Only the key-UPs of a pure Ctrl+Shift hold are
/// swallowed; every key-DOWN and any Alt/Win/other-key sequence passes through
/// untouched. Install on the UI thread (message pump); the hook proc never logs
/// (perf) and never throws — any failure falls through to CallNextHookEx.
/// Wiring (integrator): Start/Stop from PeekDowsAppContext with
/// isGuardEnabled = () => settings.PreventLayoutSwitchWhileGesturing and
/// isDirectionalFocusEnabled = () => settings.DirectionalFocusEnabled.
/// </summary>
public sealed class KeyboardLayoutGuardService : IDisposable
{
    private readonly LayoutSwitchGuard _guard = new();
    private readonly Func<bool> _isGuardEnabled;
    private readonly Func<bool> _isDirectionalFocusEnabled;
    private readonly FileLogger? _logger;

    // Anti-GC: the delegate must stay rooted while the hook is installed.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private long _swallowedCount;
    private bool _disposed;

    /// <summary>
    /// Spec shorthand "ctor(FileLogger? logger=null, Func&lt;bool&gt; isEnabled)"
    /// cannot compile as written (required param after optional), so both
    /// predicates are explicit here: guard toggle + Directional Focus toggle,
    /// evaluated on every hook event (no SetDirectionalFocusEnabled needed).
    /// </summary>
    public KeyboardLayoutGuardService(
        Func<bool> isGuardEnabled,
        Func<bool> isDirectionalFocusEnabled,
        FileLogger? logger = null)
    {
        _isGuardEnabled = isGuardEnabled ?? (() => true);
        _isDirectionalFocusEnabled = isDirectionalFocusEnabled ?? (() => true);
        _logger = logger;
        _proc = HookProc;
    }

    /// <summary>Number of key-UPs swallowed since Start (diagnostic counter).</summary>
    public long SwallowedCount => Interlocked.Read(ref _swallowedCount);

    /// <summary>
    /// Installs the low-level keyboard hook on the calling thread (call from the
    /// UI thread). No-op when already installed.
    /// </summary>
    public void Start()
    {
        if (_disposed || _hook != IntPtr.Zero)
            return;

        try
        {
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        }
        catch (Exception ex)
        {
            _hook = IntPtr.Zero;
            _logger?.Warn($"KeyboardLayoutGuard: install threw: {ex.Message}");
            return;
        }

        if (_hook != IntPtr.Zero)
            _logger?.Info("KeyboardLayoutGuard: WH_KEYBOARD_LL hook installed.");
        else
            _logger?.Warn("KeyboardLayoutGuard: WH_KEYBOARD_LL install failed.");
    }

    /// <summary>Removes the hook. No-op when not installed.</summary>
    public void Stop()
    {
        var h = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (h == IntPtr.Zero)
            return;

        try
        {
            NativeMethods.UnhookWindowsHookEx(h);
            _logger?.Info("KeyboardLayoutGuard: hook removed.");
        }
        catch
        {
            // Unhook must never crash shutdown.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode < 0)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;
            if (!isDown && !isUp)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool directionalFocusEnabled;
            bool guardEnabled;
            try
            {
                directionalFocusEnabled = _isDirectionalFocusEnabled();
                guardEnabled = _isGuardEnabled();
            }
            catch
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            var decision = _guard.Feed(info.vkCode, isDown, directionalFocusEnabled, guardEnabled);
            if (!isDown && decision == GuardDecision.Swallow)
            {
                Interlocked.Increment(ref _swallowedCount);
                return (IntPtr)1;
            }

            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        catch
        {
            // The hook proc must never crash or block input: fall through.
            try
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
