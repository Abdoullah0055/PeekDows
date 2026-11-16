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

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    internal uint Length;
    internal uint Flags;
    internal uint ShowCommand;
    internal System.Drawing.Point MinPosition;
    internal System.Drawing.Point MaxPosition;
    internal RECT NormalPosition;
}

public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
