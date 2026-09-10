using System;
using System.Runtime.InteropServices;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public class MonitorService : IMonitorResolver
{
    private readonly FileLogger? _logger;

    public MonitorService() { }

    public MonitorService(FileLogger? logger)
    {
        _logger = logger;
    }

    public MonitorInfo GetPrimaryMonitor()
    {
        var hMonitor = MonitorNativeMethods.MonitorFromWindow(IntPtr.Zero, MonitorNativeMethods.MONITOR_DEFAULTTOPRIMARY);
        return BuildMonitorInfo(hMonitor, true);
    }

    public MonitorInfo GetMonitorForWindow(IntPtr hwnd)
    {
        var hMonitor = MonitorNativeMethods.MonitorFromWindow(hwnd, MonitorNativeMethods.MONITOR_DEFAULTTONEAREST);
        return BuildMonitorInfo(hMonitor, false);
    }

    public Rect GetWorkArea(MonitorInfo monitor)
    {
        return monitor.WorkArea;
    }

    private MonitorInfo BuildMonitorInfo(IntPtr hMonitor, bool assumePrimary)
    {
        var mi = new MonitorNativeMethods.MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);

        if (MonitorNativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            bool isPrimary = assumePrimary || (mi.dwFlags & 1) != 0;
            _logger?.Info($"GetMonitorInfo succeeded: device={mi.szDevice}, workArea=({mi.rcWork.left},{mi.rcWork.top},{mi.rcWork.right},{mi.rcWork.bottom}), isPrimary={isPrimary}");
            return new MonitorInfo
            {
                Handle = hMonitor,
                WorkArea = new Rect(
                    mi.rcWork.left,
                    mi.rcWork.top,
                    mi.rcWork.right - mi.rcWork.left,
                    mi.rcWork.bottom - mi.rcWork.top
                ),
                FullArea = new Rect(
                    mi.rcMonitor.left,
                    mi.rcMonitor.top,
                    mi.rcMonitor.right - mi.rcMonitor.left,
                    mi.rcMonitor.bottom - mi.rcMonitor.top
                ),
                IsPrimary = isPrimary
            };
        }

        int win32Error = Marshal.GetLastWin32Error();
        _logger?.Error($"GetMonitorInfo failed: hMonitor={hMonitor}, win32Error={win32Error}, using fallback 1920x1080 (marked IsFallback; not usable for layout/focus decisions). Arrange will skip windows on this monitor — check display driver / RDP.");

        // B5 fix: fallback is non-silent — caller (MultiMonitorLayoutService) already skips IsFallback windows and logs Warn per-window.
        // The fallback is kept for display/debug only. IsFallback=true lets the layout and
        // Directional Focus paths reject it instead of collapsing unrelated windows into one
        // synthetic 1920x1080 monitor bucket used for focus decisions.
        return new MonitorInfo
        {
            Handle = hMonitor,
            WorkArea = new Rect(0, 0, 1920, 1080),
            FullArea = new Rect(0, 0, 1920, 1080),
            IsPrimary = true,
            IsFallback = true
        };
    }
}
