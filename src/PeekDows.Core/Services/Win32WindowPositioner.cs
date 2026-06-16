using System;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

internal sealed class Win32WindowPositioner : IWindowPositioner
{
    public bool SetWindowPosition(IntPtr hwnd, Rect rect)
    {
        uint flags = (uint)(NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        return NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, flags);
    }
}
