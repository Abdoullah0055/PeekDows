using System;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public interface IMonitorResolver
{
    MonitorInfo GetMonitorForWindow(IntPtr hwnd);
    MonitorInfo GetPrimaryMonitor();
}
