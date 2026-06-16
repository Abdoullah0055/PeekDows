using System;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

internal interface IWindowPositioner
{
    bool SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false);
}
