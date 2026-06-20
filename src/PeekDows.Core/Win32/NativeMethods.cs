using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PeekDows.Core.Win32;

public static class NativeMethods
{
    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOMOVE = 0x0002;
    public const int SWP_NOSIZE = 0x0001;
    public const int SWP_SHOWWINDOW = 0x0040;
    public const int SWP_NOSENDCHANGING = 0x0400;
    /// <summary>
    /// SetWindowPos returns without waiting for the target window's thread to process the
    /// move — i.e. a hung target cannot block the caller. Used for all placements so a slow
    /// or frozen app can never freeze PeekDows's UI thread during arrange.
    /// </summary>
    public const int SWP_ASYNCWINDOWPOS = 0x4000;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public const int WM_HOTKEY = 0x0312;

    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int VK_SPACE = 0x20;
    public const int VK_P = 0x50;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Non-blocking variant of <see cref="ShowWindow"/>: posts the command to the target
    /// thread's queue without waiting for it to be processed. Used for restoring minimised
    /// windows before placement/activation so a hung target cannot block the caller.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_SHIFT = 0x10;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    /// <summary>
    /// Sends the specified message to a window, but returns (or fails) after the given
    /// timeout if the target thread is not pumping its message queue — i.e. the window is
    /// hung. Used by Directional Focus to detect unresponsive windows before attempting any
    /// blocking activation call (AttachThreadInput / SetForegroundWindow / SetFocus), so a
    /// frozen target can never freeze PeekDows's own UI thread.
    /// </summary>
    public const uint WM_NULL = 0x0000;

    /// <summary>Return without timing out even if the window is hung (treat as failure).</summary>
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>Keep blocking for the full timeout even if the window responds later.</summary>
    public const uint SMTO_BLOCK = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);
}
