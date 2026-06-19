using System;
using System.Runtime.InteropServices;
using System.Text;
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
    private readonly WindowActivationService _activationService;
    private readonly IVirtualDesktopService _virtualDesktopService;
    private readonly Func<int> _getThresholdPx;
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
        WindowActivationService activationService,
        IVirtualDesktopService virtualDesktopService,
        Func<int> getThresholdPx,
        FileLogger? logger = null)
    {
        _gestureDetector = gestureDetector;
        _registry = registry;
        _monitorResolver = monitorResolver;
        _activationService = activationService;
        _virtualDesktopService = virtualDesktopService;
        _getThresholdPx = getThresholdPx;
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
        int dx = current.X - _anchor.X;
        int dy = current.Y - _anchor.Y;

        var threshold = _getThresholdPx();
        var slot = _gestureDetector.Detect(_anchor.X, _anchor.Y, current.X, current.Y, threshold);

        if (slot == null) return;

        if (slot == _lastTriggeredSlot) return;

        if ((DateTime.Now - _lastFocusTime).TotalMilliseconds < CooldownMs) return;

        var mouseWorkArea = MonitorFromPointNative(current.X, current.Y);

        _logger?.Info($"Directional focus trigger candidate: slot={slot.Value}, dx={dx}, dy={dy}, anchor=({_anchor.X},{_anchor.Y}), current=({current.X},{current.Y}), mouseMonitor={mouseWorkArea}, threshold={threshold}");

        if (!_registry.HasSlotsForMonitor(mouseWorkArea))
        {
            _logger?.Info($"Directional focus ignored: no slots for mouseMonitor={mouseWorkArea}");
            return;
        }

        var hwnd = _registry.GetHwndForSlot(mouseWorkArea, slot.Value);
        if (hwnd == null || hwnd == IntPtr.Zero)
        {
            _logger?.Info($"Directional focus ignored: no window mapped for slot={slot.Value} on monitor={mouseWorkArea}");
            return;
        }

        bool onCurrentDesktop = _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd.Value);
        if (!onCurrentDesktop)
        {
            _logger?.Info($"Directional focus ignored: target is not on current virtual desktop, hwnd={hwnd.Value}");
            return;
        }

        var targetTitle = GetWindowTitle(hwnd.Value);
        var targetPid = GetWindowProcessId(hwnd.Value);
        bool isIconic = NativeMethods.IsIconic(hwnd.Value);
        var currentForeground = NativeMethods.GetForegroundWindow();

        _logger?.Info($"Directional focus target: hwnd={hwnd.Value}, title={targetTitle}, pid={targetPid}, onCurrentDesktop={onCurrentDesktop}, isMinimized={isIconic}, currentForegroundHwnd={currentForeground}");

        bool activated = _activationService.Activate(hwnd.Value);

        if (activated)
        {
            _logger?.Info($"Directional focus success: slot={slot.Value}, hwnd={hwnd.Value}, title={targetTitle}");
        }
        else
        {
            var finalForeground = NativeMethods.GetForegroundWindow();
            _logger?.Warn($"Directional focus activation failed: slot={slot.Value}, hwnd={hwnd.Value}, foregroundHwnd={finalForeground}");
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

    private static string GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return "<unknown>";
        }
    }

    private static uint GetWindowProcessId(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid;
        }
        catch
        {
            return 0;
        }
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
