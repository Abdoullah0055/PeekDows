using System;
using System.Runtime.InteropServices;

namespace PeekDows.App.Tray;

/// <summary>
/// Minimal DWM interop used to opt the tray menu into native Windows 11 rounded
/// corners. Kept separate from PeekDows.Core.Win32.NativeMethods on purpose:
/// Core is the window-management P/Invoke surface and does not reference
/// WinForms; this is a purely cosmetic UI concern owned by the App project.
/// </summary>
internal static class ModernTrayWin32
{
    // DWMWA_WINDOW_CORNER_PREFERENCE (Windows 11 22000+)
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // Corner preference values
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Requests native rounded corners on the given window handle.
    /// Silently no-ops on Windows 10 (DWMWA_WINDOW_CORNER_PREFERENCE is unknown
    /// there and the call returns a non-zero HRESULT). Never throws.
    /// </summary>
    public static void TryApplyRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch
        {
            // DWM unavailable or pre-Win11: fall back to square corners. Visual only.
        }
    }
}
