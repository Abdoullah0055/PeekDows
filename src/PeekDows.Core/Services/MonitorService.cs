using System;
using System.Runtime.InteropServices;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public class MonitorService
{
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

        if (MonitorNativeMethods.GetMonitorInfoEx(hMonitor, ref mi))
        {
            bool isPrimary = assumePrimary || (mi.dwFlags & 1) != 0;
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

        return new MonitorInfo
        {
            Handle = hMonitor,
            WorkArea = new Rect(0, 0, 1920, 1080),
            FullArea = new Rect(0, 0, 1920, 1080),
            IsPrimary = true
        };
    }
}
