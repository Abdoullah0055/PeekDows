using System;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

internal sealed class Win32WindowPositioner : IWindowPositioner
{
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    public bool SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false)
    {
        uint flags;
        IntPtr hWndInsertAfter;

        if (bringToFront)
        {
            flags = (uint)(NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            hWndInsertAfter = HWND_TOP;
        }
        else
        {
            flags = (uint)(NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            hWndInsertAfter = IntPtr.Zero;
        }

        return NativeMethods.SetWindowPos(hwnd, hWndInsertAfter, rect.Left, rect.Top, rect.Width, rect.Height, flags);
    }
}
