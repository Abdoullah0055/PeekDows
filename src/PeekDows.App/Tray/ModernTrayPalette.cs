using System.Drawing;

namespace PeekDows.App.Tray;

/// <summary>
/// Centralized color palette for the modern dark tray menu.
/// Single source of truth shared by the renderer, the icon generator and the
/// status indicator so every surface stays visually consistent.
/// </summary>
internal static class ModernTrayPalette
{
    // Core surfaces (Windows 11 inspired dark theme)
    public static readonly Color Background = Color.FromArgb(0x1F, 0x1F, 0x1F);
    public static readonly Color Hover = Color.FromArgb(0x2B, 0x2B, 0x2B);
    public static readonly Color Border = Color.FromArgb(0x3A, 0x3A, 0x3A);
    public static readonly Color Separator = Color.FromArgb(0x3A, 0x3A, 0x3A);

    // Text
    public static readonly Color Text = Color.FromArgb(0xF2, 0xF2, 0xF2);
    public static readonly Color Muted = Color.FromArgb(0xA8, 0xA8, 0xA8);

    // Accent (fixed Win11 blue) for modern checks / active toggles
    public static readonly Color Accent = Color.FromArgb(0x4C, 0xC2, 0xFF);
    public static readonly Color AccentForeground = Color.White;

    // Status dot
    public static readonly Color StatusRunning = Color.FromArgb(0x6C, 0xCB, 0x5F);
}
