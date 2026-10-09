using System;
using System.Diagnostics;
using System.Linq;
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
/// foreground window + its thread's HKL every 250ms, consumes the guard's
/// third-key diagnostic, and correlates everything with the Directional Focus
/// gesture bounds. Never throws (every tick path is guarded), never logs
/// keystroke contents — only layout names, process names, window titles and
/// the armed-chord third key. Always active, even when Directional Focus is
/// off, so layout flips stay explainable in all configurations.
/// </summary>
public sealed class ForegroundWatchService : IDisposable
{
    private const int PollIntervalMs = 250;
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

    // Modifier edge journal cache: replaced every tick from guard.DrainEdges()
    // (drained even when no flip, so the buffer never goes stale); rendered
    // on KB_LAYOUT_FLIP lines only.
    private IReadOnlyList<ChordEdge> _cachedEdges = Array.Empty<ChordEdge>();

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
            _logger?.Info($"GESTURE_HKL_START hkl={CurrentLayoutName()} swallowed={swallowed}{ForegroundLocationSuffix()}{ShiftCapsSuffix()}");
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
            _logger?.Info($"GESTURE_HKL_END hkl={CurrentLayoutName()} swallowedDelta={delta}{ForegroundLocationSuffix()}{ShiftCapsSuffix()}");
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
                _logger?.Info($"CHORD_THIRD_KEY vk={keyName} inj={third.Value.Injected} ageMs={ageMs} gesture={_gestureActive}");
            }

            var chord = SafeTakeAltShiftChord();
            if (chord.HasValue)
            {
                try
                {
                    long chordAgeMs = Math.Max(0, (long)(DateTime.UtcNow - chord.Value.AtUtc).TotalMilliseconds);
                    _logger?.Info($"ALT_SHIFT_CHORD inj={chord.Value.Injected} ageMs={chordAgeMs} gesture={_gestureActive}");
                }
                catch
                {
                }
            }

            // Drain the modifier edge journal every tick into the cache
            // (rendered on flip lines only, so the buffer never goes stale).
            try
            {
                _cachedEdges = _guard.DrainEdges() ?? Array.Empty<ChordEdge>();
            }
            catch
            {
                _cachedEdges = Array.Empty<ChordEdge>();
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
                string hookState;
                try
                {
                    hookState = _guard.IsHookInstalled ? "ok" : "gone";
                }
                catch
                {
                    hookState = "gone";
                }
                _logger?.Info($"KB_LAYOUT_FLIP {oldName} -> {newName} hwnd=0x{hwnd.ToInt64():X} tid={tid} sameHwnd={sameHwnd} hook={hookState} fg=\"{title}\" proc={procName} pid={pid} gesture={_gestureActive} swallowedDelta={swallowedDelta} {FormatChordEdges(_cachedEdges)}");
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

    private (int Vk, DateTime AtUtc, bool Injected)? SafeTakeThirdKey()
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

    private (DateTime AtUtc, bool Injected)? SafeTakeAltShiftChord()
    {
        try
        {
            return _guard.TakeAltShiftChord();
        }
        catch
        {
            return null;
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

    /// <summary>
    /// Renders the drained modifier edge buffer as "edges=[Nom-down@12ms,…]"
    /// (whole buffer, capacity 40; per-edge try/catch). Never throws.
    /// </summary>
    private static string FormatChordEdges(IReadOnlyList<ChordEdge> edges)
    {
        try
        {
            if (edges == null || edges.Count == 0)
                return "edges=[]";
            long nowTicks = DateTime.UtcNow.Ticks;
            var parts = new string[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                try
                {
                    var e = edges[i];
                    string name;
                    try
                    {
                        name = KeyboardLayoutMonitor.KeyName(e.Vk);
                    }
                    catch
                    {
                        name = $"0x{e.Vk:X2}";
                    }
                    long ageMs = (nowTicks - e.TicksUtc) / TimeSpan.TicksPerMillisecond;
                    if (ageMs < 0)
                        ageMs = 0;
                    parts[i] = $"{name}-{(e.KeyDown ? "down" : "up")}@{ageMs}ms";
                }
                catch
                {
                    parts[i] = "?";
                }
            }
            return $"edges=[{string.Join(",", parts)}]";
        }
        catch
        {
            return "edges=[]";
        }
    }
}
