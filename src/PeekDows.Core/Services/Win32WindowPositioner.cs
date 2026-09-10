using System;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

internal sealed class Win32WindowPositioner : IWindowPositioner
{
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    public (bool success, int win32Error) SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false)
    {
        uint flags;
        IntPtr hWndInsertAfter;
        if (bringToFront)
        {
            flags = (uint)(NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_ASYNCWINDOWPOS);
            hWndInsertAfter = HWND_TOP;
        }
        else
        {
            flags = (uint)(NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_ASYNCWINDOWPOS);
            hWndInsertAfter = IntPtr.Zero;
        }
        bool ok = NativeMethods.SetWindowPos(hwnd, hWndInsertAfter, rect.Left, rect.Top, rect.Width, rect.Height, flags);
        int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return (ok, err);
    }

    public (bool success, int win32Error) MaximizeWindow(IntPtr hwnd)
    {
        bool ok = NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MAXIMIZE);
        // ShowWindowAsync currently has no SetLastError=true — capture defensively.
        int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return (ok, err);
    }
}
