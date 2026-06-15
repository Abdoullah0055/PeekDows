using System;

namespace PeekDows.Core.Models;

public sealed class RawWindowInfo
{
    public IntPtr Hwnd { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public Rect CurrentRect { get; init; }
    public bool IsVisible { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsMaximized { get; init; }
    public bool IsForeground { get; init; }
    public bool IsCloaked { get; init; }
}
