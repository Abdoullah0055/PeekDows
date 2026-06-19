using System;

namespace PeekDows.Core.Models;

public sealed record ArrangedWindowSlot(
    IntPtr Hwnd,
    DirectionalFocusSlot Slot,
    Rect MonitorWorkArea
);
