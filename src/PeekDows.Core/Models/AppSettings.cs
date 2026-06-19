using System.Collections.Generic;

namespace PeekDows.Core.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool AutoArrange { get; set; } = false;
    public bool ArrangeOnStartup { get; set; } = false;
    public bool ArrangeAfterWindowCloses { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool ShowTrayNotifications { get; set; } = false;
    
    public string SingleWindowMode { get; set; } = "FullWorkArea";
    public string OverflowBehavior { get; set; } = "Ignore";
    
    public int ManualMoveCooldownMs { get; set; } = 5000;
    public int NewWindowStabilizationDelayMs { get; set; } = 700;
    public int WindowDetectionIntervalMs { get; set; } = 1000;
    
    public bool BringFocusedWindowToFront { get; set; } = true;
    public bool RearrangeOnFocus { get; set; } = false;

    public bool DirectionalFocusEnabled { get; set; } = true;
    public int DirectionalFocusThresholdPx { get; set; } = 80;

    /// <summary>
    /// When false (default), windows that are truly maximized via Windows are skipped during
    /// arrange. When true, maximized windows are restored and repositioned like normal windows.
    /// This only affects genuinely maximized windows; near-fullscreen non-maximized windows are
    /// always arrangeable.
    /// </summary>
    public bool AllowRepositionMaximizedWindows { get; set; } = false;

    public List<string> IgnoredProcesses { get; set; } = new()
    {
        "PeekDows.exe",
        "SystemSettings.exe"
    };

    public List<string> IgnoredClasses { get; set; } = new();

    public Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        { "arrangeNow", "Ctrl+Alt+Space" },
        { "pauseResume", "Ctrl+Alt+P" },
        { "assignSlotA", "Ctrl+Alt+1" },
        { "assignSlotB", "Ctrl+Alt+2" },
        { "assignSlotC", "Ctrl+Alt+3" },
        { "assignSlotD", "Ctrl+Alt+4" }
    };
}
