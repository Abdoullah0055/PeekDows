using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

/// <summary>
/// One modifier edge kept for layout-flip diagnosis (compat surface for
/// <c>ForegroundWatchService</c>). The observe-only tap hook never records any
/// edge, so drains are always empty — kept only so the watcher's log lines
/// keep compiling unchanged (they render "edges=[]", proving nothing was
/// swallowed).
/// </summary>
public sealed record ChordEdge(int Vk, bool KeyDown, bool Injected, long TicksUtc);

/// <summary>
/// WH_KEYBOARD_LL tap detector that arms the Directional Focus gesture on a
/// Ctrl double-tap. STRICTLY OBSERVE-ONLY: the hook proc feeds every key event
/// to <see cref="DoubleTapDetector"/> and raises <see cref="Armed"/> with the
/// cursor position when the second tap completes, but it NEVER swallows input —
/// every path ends in <c>CallNextHookEx</c>. The hook proc never logs (perf)
/// and never throws — any failure falls through to CallNextHookEx.
/// Wiring (integrator): Start/Stop from PeekDowsAppContext coupled to
/// DirectionalFocusEnabled; <c>Armed += pt => _directionalFocusService.ArmGesture(pt)</c>.
/// </summary>
public sealed class DirectionalTapService : IDisposable
{
    private readonly DoubleTapDetector _detector = new();
    private readonly FileLogger? _logger;

    // Anti-GC: the delegate must stay rooted while the hook is installed.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private long _armedCount;
    private bool _disposed;

    /// <summary>
    /// Raised (on the installing thread, i.e. the UI thread) when a Ctrl
    /// double-tap completes, with the cursor position as gesture anchor.
    /// Subscribers must never throw (service swallows subscriber errors).
    /// </summary>
    public event Action<Point>? Armed;

    public DirectionalTapService(FileLogger? logger = null)
    {
        _logger = logger;
        _proc = HookProc;
    }

    /// <summary>Number of double-taps armed since construction (diagnostic counter).</summary>
    public long ArmedCount => Interlocked.Read(ref _armedCount);

    /// <summary>
    /// Compat surface for <c>ForegroundWatchService</c>: the observe-only hook
    /// swallows nothing, so this is always 0. The watcher's GESTURE_HKL_*
    /// lines keep logging <c>swallowedDelta=0</c> as proof.
    /// </summary>
    public long SwallowedCount => 0;

    /// <summary>
    /// Compat surface for <c>ForegroundWatchService</c>: no third-key tracking
    /// anymore (nothing is swallowed, no chord to explain). Always null.
    /// </summary>
    public (int Vk, DateTime AtUtc, bool Injected)? TakeThirdKeyDown() => null;

    /// <summary>
    /// Compat surface for <c>ForegroundWatchService</c>: no Alt+Shift chord
    /// tracking anymore. Always null.
    /// </summary>
    public (DateTime AtUtc, bool Injected)? TakeAltShiftChord() => null;

    /// <summary>
    /// Compat surface for <c>ForegroundWatchService</c>: no modifier edge
    /// journal anymore. Always empty, never throws.
    /// </summary>
    public IReadOnlyList<ChordEdge> DrainEdges()
    {
        try
        {
            return Array.Empty<ChordEdge>();
        }
        catch
        {
            return Array.Empty<ChordEdge>();
        }
    }

    /// <summary>True while the low-level keyboard hook is installed.</summary>
    public bool IsHookInstalled => _hook != IntPtr.Zero;

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
            _detector.Reset();
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        }
        catch (Exception ex)
        {
            _hook = IntPtr.Zero;
            _logger?.Warn($"DirectionalTap: install threw: {ex.Message}");
            return;
        }

        if (_hook != IntPtr.Zero)
            _logger?.Info("DirectionalTap: WH_KEYBOARD_LL hook installed (observe-only).");
        else
            _logger?.Warn("DirectionalTap: WH_KEYBOARD_LL install failed.");
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
            _logger?.Info("DirectionalTap: hook removed.");
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

            TapResult result;
            try
            {
                result = _detector.Feed(info.vkCode, isDown, DateTime.UtcNow.Ticks);
            }
            catch
            {
                // Detector must never disturb the hook: fall through.
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            if (result == TapResult.Armed)
            {
                Interlocked.Increment(ref _armedCount);
                Point anchor;
                try
                {
                    anchor = Cursor.Position;
                }
                catch
                {
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                }
                try { Armed?.Invoke(anchor); } catch { }
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
