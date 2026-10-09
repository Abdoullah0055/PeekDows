using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PeekDows.Core.Models;

public sealed class AppSettings
{
    /// <summary>
    /// Schema version used by <see cref="PeekDows.Core.Services.SettingsService.MigrateIfNeeded"/>
    /// to apply one-time migrations. Bump when a breaking default change needs to be pushed
    /// to existing settings.json files.
    /// </summary>
    public int Version { get; set; } = 5;
    public bool Enabled { get; set; } = true;
    public bool AutoArrange { get; set; } = false;
    public bool ArrangeOnStartup { get; set; } = false;
    public bool ArrangeAfterWindowCloses { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool ShowTrayNotifications { get; set; } = false;
    
    // B7: OverflowBehavior was never implemented — kept for compat but deprecated.
    // New code must not read it; migration warns if set to non-default.
    [Obsolete("OverflowBehavior is deprecated and has no effect. It will be removed in a future version.")]
    public string OverflowBehavior { get; set; } = "Ignore";

    public int ManualMoveCooldownMs { get; set; } = 5000;
    public int NewWindowStabilizationDelayMs { get; set; } = 700;
    public int WindowDetectionIntervalMs { get; set; } = 1000;
    
    public bool BringFocusedWindowToFront { get; set; } = true;
    public bool RearrangeOnFocus { get; set; } = false;

    public bool DirectionalFocusEnabled { get; set; } = true;
    public int DirectionalFocusThresholdPx { get; set; } = 50;

    /// <summary>
    /// When true (default), a WH_KEYBOARD_LL hook swallows the key-UPs of a pure
    /// Ctrl+Shift hold so Windows never sees the "toggle keyboard layout" chord
    /// complete (FR↔EN flip) while gesturing. Only pure-chord releases are
    /// blocked; every key-DOWN and any Alt/Win/other-key sequence passes through.
    /// JSON name is "preventLayoutSwitch" (camelCase UI contract).
    /// </summary>
    [JsonPropertyName("preventLayoutSwitch")]
    public bool PreventLayoutSwitchWhileGesturing { get; set; } = true;

    /// <summary>
    /// When false (default), windows that are truly maximized via Windows are skipped during
    /// arrange. When true, maximized windows are restored and repositioned like normal windows.
    /// This only affects genuinely maximized windows; near-fullscreen non-maximized windows are
    /// always arrangeable.
    /// </summary>
    public bool AllowRepositionMaximizedWindows { get; set; } = false;

    /// <summary>
    /// When true (default), placements are animated (150ms ease-out tween; maximize uses
    /// tween-then-snap). When false, placements apply instantly — the exact pre-animation
    /// behavior. Toggleable from the tray menu.
    /// </summary>
    public bool AnimateWindowTransitions { get; set; } = true;

    /// <summary>
    /// Focus hint display mode: Off | Overlay (default).
    /// JSON name is "focusHintMode" (camelCase UI contract).
    /// </summary>
    [JsonPropertyName("focusHintMode")]
    public string FocusHintMode { get; set; } = "Overlay";

    public WindowSizePreset WindowSizePreset { get; set; } = WindowSizePreset.Small;

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
