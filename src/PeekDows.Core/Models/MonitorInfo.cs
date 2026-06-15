using System;

namespace PeekDows.Core.Models;

public sealed class MonitorInfo
{
    public IntPtr Handle { get; init; }
    public Rect WorkArea { get; init; }
    public Rect FullArea { get; init; }
    public bool IsPrimary { get; init; }
}
