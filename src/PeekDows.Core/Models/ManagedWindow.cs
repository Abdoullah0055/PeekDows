using System;

namespace PeekDows.Core.Models;

public sealed class ManagedWindow
{
    public IntPtr Hwnd { get; init; }
    public string Title { get; set; } = "";
    public string ClassName { get; init; } = "";
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public Rect CurrentRect { get; set; }
    public bool IsVisible { get; set; }
    public bool IsMinimized { get; set; }
    public bool IsMaximized { get; set; }
    public bool IsForeground { get; set; }
    public DateTime FirstSeenAt { get; init; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? LastFocusedAt { get; set; }
    public string? AssignedSlotId { get; set; }
    public bool IsPinned { get; set; }
    public bool IsIgnored { get; set; }
    
    public static ManagedWindow FromRaw(RawWindowInfo raw, DateTime now)
    {
        return new ManagedWindow
        {
            Hwnd = raw.Hwnd,
            Title = raw.Title,
            ClassName = raw.ClassName,
            ProcessId = raw.ProcessId,
            ProcessName = raw.ProcessName,
            CurrentRect = raw.CurrentRect,
            IsVisible = raw.IsVisible,
            IsMinimized = raw.IsMinimized,
            IsMaximized = raw.IsMaximized,
            IsForeground = raw.IsForeground,
            FirstSeenAt = now,
            LastSeenAt = now
        };
    }
}
