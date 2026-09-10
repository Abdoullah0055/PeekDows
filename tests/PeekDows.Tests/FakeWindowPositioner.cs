using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class FakeWindowPositioner : IWindowPositioner
{
    private readonly HashSet<IntPtr> _failHwnds;
    public List<(IntPtr Hwnd, Rect Rect, bool BringToFront)> Calls { get; } = [];

    public FakeWindowPositioner()
    {
        _failHwnds = new HashSet<IntPtr>();
    }

    public FakeWindowPositioner(IEnumerable<IntPtr> failHwnds)
    {
        _failHwnds = new HashSet<IntPtr>(failHwnds);
    }

    public (bool success, int win32Error) SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false)
    {
        Calls.Add((hwnd, rect, bringToFront));
        bool ok = !_failHwnds.Contains(hwnd);
        return (ok, ok ? 0 : 5); // 5 = ACCESS_DENIED fake error
    }

    public List<IntPtr> MaximizeCalls { get; } = [];

    public (bool success, int win32Error) MaximizeWindow(IntPtr hwnd)
    {
        MaximizeCalls.Add(hwnd);
        bool ok = !_failHwnds.Contains(hwnd);
        return (ok, ok ? 0 : 5);
    }
}
