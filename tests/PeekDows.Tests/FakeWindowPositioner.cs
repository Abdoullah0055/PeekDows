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

    public bool SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false)
    {
        Calls.Add((hwnd, rect, bringToFront));
        return !_failHwnds.Contains(hwnd);
    }
}
