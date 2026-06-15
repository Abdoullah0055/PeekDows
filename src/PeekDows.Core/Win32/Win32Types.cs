using System;
using System.Runtime.InteropServices;

namespace PeekDows.Core.Win32;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
