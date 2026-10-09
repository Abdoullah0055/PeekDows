using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Diagnostics;

/// <summary>
/// UI-thread foreground watcher that traces every keyboard-layout flip and
/// every Task Manager open into peekdows.log. Polling-only design: no keyboard
/// hook is installed anywhere (the Ctrl+Win hold gesture has no OS layout
/// toggle). Never throws (every tick path is guarded), never logs keystroke
/// contents — only layout names, process names and window titles. Always
/// active, even when Directional Focus is off, so layout flips stay
/// explainable in all configurations.
/// </summary>
public sealed class ForegroundWatchService : IDisposable
{
    private const int PollIntervalMs = 250;
    private const int MaxTitleChars = 40;

    private readonly FileLogger? _logger;
    private readonly Func<bool> _isEnabled;
    private readonly System.Windows.Forms.Timer _timer;

    private LayoutSample? _prev;
    private IntPtr _lastHwnd = IntPtr.Zero;
    private string _lastProcName = "?";
    private bool _gestureActive;
    private bool _disposed;

    public ForegroundWatchService(
        FileLogger? logger,
        Func<bool>? isEnabled = null)
    {
        _logger = logger;
        _isEnabled = isEnabled ?? (() => true);
        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Logs the OS toggle-hotkey registry state, samples the initial layout
    /// (no flip logged for it), then starts the 250ms poll timer.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            return;

        try
        {
            string cuState = "absent";
            string cuLang = "absent";
            string cuLay = "absent";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Toggle");
                if (key != null)
                {
                    cuState = "present";
                    cuLang = key.GetValue("Language Hotkey")?.ToString() ?? "absent";
                    cuLay = key.GetValue("Layout Hotkey")?.ToString() ?? "absent";
                }
            }
            catch
            {
                // Registry unreadable: keep "absent" placeholders.
            }
            string defState = "absent";
            string defLang = "absent";
            string defLay = "absent";
            try
            {
                using var key = Registry.Users.OpenSubKey(@".DEFAULT\Keyboard Layout\Toggle");
                if (key != null)
                {
                    defState = "present";
                    defLang = key.GetValue("Language Hotkey")?.ToString() ?? "absent";
                    defLay = key.GetValue("Layout Hotkey")?.ToString() ?? "absent";
                }
            }
            catch
            {
                // Registry unreadable: keep "absent" placeholders.
            }
            _logger?.Info($"KB_TOGGLE_KEYS cu={cuState} lang={cuLang} lay={cuLay} def={defState} lang={defLang} lay={defLay}");
        }
        catch
        {
        }

        try
        {
            var names = InputLanguage.InstalledInputLanguages
                .Cast<InputLanguage>()
                .Select(l =>
                {
                    try
                    {
                        return l.Culture?.Name ?? "?";
                    }
                    catch
                    {
                        return "?";
                    }
                })
                .Distinct()
                .ToArray();
            _logger?.Info($"KB_LAYOUTS <{string.Join(",", names)}>");
        }
        catch
        {
        }

        try
        {
            string sticky;
            try
            {
                var sk = new NativeMethods.STICKYKEYS { cbSize = 8 };
                bool ok = NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0);
                sticky = ok
                    ? (((sk.dwFlags & NativeMethods.SKF_STICKYKEYSON) != 0) ? "on" : "off")
                    : "?";
            }
            catch
            {
                sticky = "?";
            }
            string caps;
            try
            {
                caps = ((NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0) ? "on" : "off";
            }
            catch
            {
                caps = "?";
            }
            _logger?.Info($"KB_OS_STATE sticky={sticky} caps={caps}");
        }
        catch
        {
        }

        // Initial sample: establishes the baseline so the first tick never
        // reports a spurious flip. _prev stays null when sampling fails.
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
                IntPtr hkl = NativeMethods.GetKeyboardLayout(tid);
                _prev = new LayoutSample(hkl, tid);
            }
        }
        catch
        {
        }

        try
        {
            _timer.Start();
            _logger?.Info("KBWatch started");
        }
        catch
        {
        }
    }

    /// <summary>Stops the poll timer.</summary>
    public void Stop()
    {
        try
        {
            _timer.Stop();
            _logger?.Info("KBWatch stopped");
        }
        catch
        {
        }
    }

    /// <summary>Marks the start of a Directional Focus gesture bound.</summary>
    public void OnGestureStarted()
    {
        _gestureActive = true;
        try
        {
            _logger?.Info($"GESTURE_HKL_START hkl={CurrentLayoutName()}{ForegroundLocationSuffix()}{ShiftCapsSuffix()}");
        }
        catch
        {
        }
    }

    /// <summary>Marks the end of a Directional Focus gesture bound.</summary>
    public void OnGestureEnded()
    {
        try
        {
            _logger?.Info($"GESTURE_HKL_END hkl={CurrentLayoutName()}{ForegroundLocationSuffix()}{ShiftCapsSuffix()}");
        }
        catch
        {
        }
        _gestureActive = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer.Dispose();
        }
        catch
        {
        }
        GC.SuppressFinalize(this);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_disposed)
                return;
            try
            {
                if (!_isEnabled())
                    return;
            }
            catch
            {
                return;
            }

            IntPtr hwnd;
            try
            {
                hwnd = NativeMethods.GetForegroundWindow();
            }
            catch
            {
                return;
            }
            if (hwnd == IntPtr.Zero)
                return;

            uint pid = 0;
            uint tid;
            try
            {
                tid = NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            }
            catch
            {
                return;
            }

            IntPtr hkl;
            try
            {
                hkl = NativeMethods.GetKeyboardLayout(tid);
            }
            catch
            {
                return;
            }

            var cur = new LayoutSample(hkl, tid);

            // Captured BEFORE _lastHwnd is updated below (proc-name cache).
            bool sameHwnd = hwnd == _lastHwnd;

            // Resolve the process name only when the foreground window changed
            // (caches _lastHwnd/_lastProcName); keep the previous tick's name
            // for Taskmgr open-edge detection.
            string prevProc = _lastProcName;
            string procName;
            if (hwnd != _lastHwnd)
            {
                try
                {
                    procName = Process.GetProcessById((int)pid).ProcessName;
                }
                catch
                {
                    procName = "?";
                }
                _lastHwnd = hwnd;
                _lastProcName = procName;
            }
            else
            {
                procName = _lastProcName;
            }

            string title = GetForegroundTitle(hwnd);

            bool flipped;
            try
            {
                flipped = KeyboardLayoutMonitor.HasChanged(_prev, cur);
            }
            catch
            {
                flipped = false;
            }
            if (flipped)
            {
                string oldName;
                try
                {
                    oldName = _prev == null ? "?" : KeyboardLayoutMonitor.LangNameFromHkl(_prev.Hkl);
                }
                catch
                {
                    oldName = "?";
                }
                string newName;
                try
                {
                    newName = KeyboardLayoutMonitor.LangNameFromHkl(cur.Hkl);
                }
                catch
                {
                    newName = "?";
                }
                _logger?.Info($"KB_LAYOUT_FLIP {oldName} -> {newName} hwnd=0x{hwnd.ToInt64():X} tid={tid} sameHwnd={sameHwnd} fg=\"{title}\" proc={procName} pid={pid} gesture={_gestureActive}");
            }

            if (string.Equals(procName, "Taskmgr", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(prevProc, "Taskmgr", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Info($"TASKMGR_OPEN fg=\"{title}\" gesture={_gestureActive} hint=(OS chord Ctrl+Shift+Esc, unrelated to the Ctrl+Win hold)");
            }

            _prev = cur;
        }
        catch
        {
            // The poll tick must never throw on the UI thread.
        }
    }

    private string CurrentLayoutName()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
                IntPtr hkl = NativeMethods.GetKeyboardLayout(tid);
                return KeyboardLayoutMonitor.LangNameFromHkl(hkl);
            }
        }
        catch
        {
        }
        try
        {
            if (_prev != null)
                return KeyboardLayoutMonitor.LangNameFromHkl(_prev.Hkl);
        }
        catch
        {
        }
        return "?";
    }

    private static string GetForegroundTitle(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            int len = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            if (len <= 0)
                return string.Empty;
            string title = sb.ToString();
            if (title.Length > MaxTitleChars)
                title = title.Substring(0, MaxTitleChars);
            return title.Replace('"', '\'');
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Samples the current foreground window/thread (" hwnd=0x… tid=…") for
    /// gesture-bound lines; returns "" when sampling fails (fallback: no suffix).
    /// </summary>
    private static string ForegroundLocationSuffix()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return string.Empty;
            uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            return $" hwnd=0x{hwnd.ToInt64():X} tid={tid}";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Physical Shift + CapsLock state suffix for gesture-bound lines
    /// (" shiftPhys=down caps=off"). Never throws (failures yield "?").
    /// </summary>
    private static string ShiftCapsSuffix()
    {
        string shift;
        try
        {
            shift = ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0) ? "down" : "up";
        }
        catch
        {
            shift = "?";
        }
        string caps;
        try
        {
            caps = ((NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0) ? "on" : "off";
        }
        catch
        {
            caps = "?";
        }
        return $" shiftPhys={shift} caps={caps}";
    }
}
