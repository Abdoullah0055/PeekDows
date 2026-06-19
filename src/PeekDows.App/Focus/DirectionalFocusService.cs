using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

public sealed class DirectionalFocusService : IDisposable
{
    private const int TickIntervalMs = 40;
    private const int CooldownMs = 250;

    private readonly DirectionalFocusGestureDetector _gestureDetector;
    private readonly DirectionalFocusRegistry _registry;
    private readonly IMonitorResolver _monitorResolver;
    private readonly FileLogger? _logger;

    private readonly System.Windows.Forms.Timer _timer;

    private bool _ctrlWasDown;
    private Point _anchor;
    private DirectionalFocusSlot? _lastTriggeredSlot;
    private DateTime _lastFocusTime = DateTime.MinValue;
    private bool _disposed;

    public DirectionalFocusService(
        DirectionalFocusGestureDetector gestureDetector,
        DirectionalFocusRegistry registry,
        IMonitorResolver monitorResolver,
        FileLogger? logger = null)
    {
        _gestureDetector = gestureDetector;
        _registry = registry;
        _monitorResolver = monitorResolver;
        _logger = logger;

        _timer = new System.Windows.Forms.Timer { Interval = TickIntervalMs };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        if (_timer.Enabled) return;
        _timer.Start();
        _logger?.Info("DirectionalFocusService started");
    }

    public void Stop()
    {
        if (!_timer.Enabled) return;
        _timer.Stop();
        ResetState();
        _logger?.Info("DirectionalFocusService stopped");
    }

    public DirectionalFocusRegistry Registry => _registry;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            _logger?.Error("DirectionalFocusService tick error", ex);
        }
    }

    internal void Tick()
    {
        bool ctrlDown = IsKeyDown(NativeMethods.VK_CONTROL);
        bool altDown = IsKeyDown(NativeMethods.VK_MENU);
        bool shiftDown = IsKeyDown(NativeMethods.VK_SHIFT);

        if (!ctrlDown || altDown || shiftDown)
        {
            if (_ctrlWasDown)
            {
                ResetState();
                _ctrlWasDown = false;
            }
            return;
        }

        if (!_ctrlWasDown)
        {
            _ctrlWasDown = true;
            _anchor = Cursor.Position;
            _lastTriggeredSlot = null;
            return;
        }

        var current = Cursor.Position;
        var slot = _gestureDetector.Detect(_anchor.X, _anchor.Y, current.X, current.Y, 80);

        if (slot == null) return;

        if (slot == _lastTriggeredSlot) return;

        if ((DateTime.Now - _lastFocusTime).TotalMilliseconds < CooldownMs) return;

        var monitor = _monitorResolver.GetMonitorForWindow(GetForegroundWindowSafe());
        var monitorWorkArea = MonitorFromPointNative(current.X, current.Y);

        if (!_registry.HasSlotsForMonitor(monitorWorkArea)) return;

        var hwnd = _registry.GetHwndForSlot(monitorWorkArea, slot.Value);
        if (hwnd == null || hwnd == IntPtr.Zero) return;

        if (NativeMethods.IsIconic(hwnd.Value))
        {
            NativeMethods.ShowWindow(hwnd.Value, NativeMethods.SW_RESTORE);
        }

        if (NativeMethods.SetForegroundWindow(hwnd.Value))
        {
            _logger?.Info($"Directional focus triggered: monitor={monitorWorkArea}, slot={slot.Value}, hwnd={hwnd.Value}");
        }
        else
        {
            _logger?.Warn($"SetForegroundWindow failed: hwnd={hwnd.Value}");
        }

        _lastTriggeredSlot = slot;
        _lastFocusTime = DateTime.Now;
    }

    private void ResetState()
    {
        _anchor = Point.Empty;
        _lastTriggeredSlot = null;
    }

    private static bool IsKeyDown(int vk)
    {
        return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static IntPtr GetForegroundWindowSafe()
    {
        try { return NativeMethods.GetForegroundWindow(); }
        catch { return IntPtr.Zero; }
    }

    private Rect MonitorFromPointNative(int x, int y)
    {
        var point = new POINT { X = x, Y = y };
        var hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);

        if (GetMonitorInfo(hMonitor, ref mi))
        {
            return new Rect(
                mi.rcWork.left,
                mi.rcWork.top,
                mi.rcWork.right - mi.rcWork.left,
                mi.rcWork.bottom - mi.rcWork.top
            );
        }

        var primary = _monitorResolver.GetPrimaryMonitor();
        return primary.WorkArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
