using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using PeekDows.App.Focus;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Diagnostics;

/// <summary>
/// UI-thread foreground watcher that traces every keyboard-layout flip and
/// every Task Manager open with its exact cause into peekdows.log. Polls the
/// foreground window + its thread's HKL every 500ms, consumes the guard's
/// third-key diagnostic, and correlates everything with the Directional Focus
/// gesture bounds. Never throws (every tick path is guarded), never logs
/// keystroke contents — only layout names, process names, window titles and
/// the armed-chord third key. Always active, even when Directional Focus is
/// off, so layout flips stay explainable in all configurations.
/// </summary>
public sealed class ForegroundWatchService : IDisposable
{
    private const int PollIntervalMs = 500;
    private const int MaxTitleChars = 40;

    private readonly KeyboardLayoutGuardService _guard;
    private readonly FileLogger? _logger;
    private readonly Func<bool> _isEnabled;
    private readonly System.Windows.Forms.Timer _timer;

    private LayoutSample? _prev;
    private IntPtr _lastHwnd = IntPtr.Zero;
    private string _lastProcName = "?";
    private long _lastSwallowed;
    private long _gestureStartSwallowed;
    private int? _lastThirdKeyVk;
    private string _lastThirdKeyName = "?";
    private DateTime _lastThirdKeyAtUtc;
    private bool _gestureActive;
    private bool _disposed;

    public ForegroundWatchService(
        KeyboardLayoutGuardService guard,
        FileLogger? logger,
        Func<bool>? isEnabled = null)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _logger = logger;
        _isEnabled = isEnabled ?? (() => true);
        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Logs the OS toggle-hotkey registry state, samples the initial layout
    /// (no flip logged for it), then starts the 500ms poll timer.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            return;

        try
        {
            string languageHotKey = "?";
            string layoutHotKey = "?";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Toggle");
                if (key != null)
                {
                    languageHotKey = key.GetValue("Language Hotkey")?.ToString() ?? "?";
                    layoutHotKey = key.GetValue("Layout Hotkey")?.ToString() ?? "?";
                }
            }
            catch
            {
                // Registry unreadable: keep "?" placeholders.
            }
            _logger?.Info($"KB_TOGGLE_KEYS languageHotKey={languageHotKey} layoutHotKey={layoutHotKey} (OS chord armed if 1/2)");
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
                _lastSwallowed = SafeSwallowedCount();
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
            long swallowed = SafeSwallowedCount();
            _gestureStartSwallowed = swallowed;
            _lastSwallowed = swallowed;
            _logger?.Info($"GESTURE_HKL_START hkl={CurrentLayoutName()} swallowed={swallowed}");
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
            long swallowed = SafeSwallowedCount();
            long delta = swallowed - _gestureStartSwallowed;
            _lastSwallowed = swallowed;
            _logger?.Info($"GESTURE_HKL_END hkl={CurrentLayoutName()} swallowedDelta={delta}");
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

            long swallowedNow = SafeSwallowedCount();
            long swallowedDelta = swallowedNow - _lastSwallowed;

            var third = SafeTakeThirdKey();
            if (third.HasValue)
            {
                string keyName;
                try
                {
                    keyName = KeyboardLayoutMonitor.KeyName(third.Value.Vk);
                }
                catch
                {
                    keyName = $"VK_{third.Value.Vk:X2}";
                }
                long ageMs = Math.Max(0, (long)(DateTime.UtcNow - third.Value.AtUtc).TotalMilliseconds);
                _lastThirdKeyVk = third.Value.Vk;
                _lastThirdKeyName = keyName;
                _lastThirdKeyAtUtc = third.Value.AtUtc;
                _logger?.Info($"CHORD_THIRD_KEY vk={keyName} ageMs={ageMs} gesture={_gestureActive}");
            }

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
                _logger?.Info($"KB_LAYOUT_FLIP {oldName} -> {newName} fg=\"{title}\" proc={procName} pid={pid} gesture={_gestureActive} swallowedDelta={swallowedDelta}");
            }

            if (string.Equals(procName, "Taskmgr", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(prevProc, "Taskmgr", StringComparison.OrdinalIgnoreCase))
            {
                string lastThird = _lastThirdKeyVk == null
                    ? "none"
                    : $"{_lastThirdKeyName}/{Math.Max(0, (long)(DateTime.UtcNow - _lastThirdKeyAtUtc).TotalMilliseconds)}ms";
                _logger?.Info($"TASKMGR_OPEN fg=\"{title}\" gesture={_gestureActive} lastThirdKey={lastThird} swallowedDelta={swallowedDelta} hint=(Esc-pendant-geste => accord OS Ctrl+Shift+Esc)");
            }

            _prev = cur;
            _lastSwallowed = swallowedNow;
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

    private long SafeSwallowedCount()
    {
        try
        {
            return _guard.SwallowedCount;
        }
        catch
        {
            return _lastSwallowed;
        }
    }

    private (int Vk, DateTime AtUtc)? SafeTakeThirdKey()
    {
        try
        {
            return _guard.TakeThirdKeyDown();
        }
        catch
        {
            return null;
        }
    }
}
