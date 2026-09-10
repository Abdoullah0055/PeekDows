using System;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

internal interface IWindowPositioner
{
    /// <returns>(success, win32Error) — win32Error captured inside the positioner immediately after the P/Invoke.</returns>
    (bool success, int win32Error) SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false);

    /// <summary>
    /// Natively maximizes the window using the NON-BLOCKING ShowWindowAsync, consistent
    /// with the restore path: a hung target cannot block the caller.
    /// </summary>
    (bool success, int win32Error) MaximizeWindow(IntPtr hwnd);

    // Backward-compat helpers for callers that ignore win32Error.
    bool SetWindowPositionLegacy(IntPtr hwnd, Rect rect, bool bringToFront = false) => SetWindowPosition(hwnd, rect, bringToFront).success;
}
