using System;
using System.Runtime.InteropServices;
using PeekDows.Core.Services;

namespace PeekDows.Core.Services;

public interface IVirtualDesktopService
{
    bool IsWindowOnCurrentVirtualDesktop(IntPtr hwnd);
}

public sealed class VirtualDesktopService : IVirtualDesktopService
{
    private readonly IVirtualDesktopManager? _manager;
    private readonly FileLogger? _logger;
    private bool _comUnavailableLogged;

    public VirtualDesktopService(FileLogger? logger = null)
    {
        _logger = logger;
        try
        {
            _manager = (IVirtualDesktopManager?)new CVirtualDesktopManager();
            _logger?.Info("VirtualDesktopService: IVirtualDesktopManager COM instance created");
        }
        catch (Exception ex)
        {
            _manager = null;
            _logger?.Warn($"VirtualDesktopService: COM unavailable, virtual desktop filtering disabled: {ex.Message}");
        }
    }

    internal VirtualDesktopService(IVirtualDesktopManager? manager, FileLogger? logger = null)
    {
        _manager = manager;
        _logger = logger;
    }

    public bool IsWindowOnCurrentVirtualDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        if (_manager == null)
        {
            if (!_comUnavailableLogged)
            {
                _comUnavailableLogged = true;
                _logger?.Warn("VirtualDesktopService: IsWindowOnCurrentVirtualDesktop returning true (COM unavailable)");
            }
            return true;
        }

        try
        {
            int hr = _manager.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrentDesktop);
            if (hr == 0)
            {
                return onCurrentDesktop;
            }

            _logger?.Warn($"VirtualDesktopService: IsWindowOnCurrentVirtualDesktop returned hr=0x{hr:X8} for hwnd={hwnd}, falling back to true");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"VirtualDesktopService: IsWindowOnCurrentVirtualDesktop exception for hwnd={hwnd}: {ex.Message}, falling back to true");
            return true;
        }
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVirtualDesktopManager
    {
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    internal class CVirtualDesktopManager
    {
    }
}
