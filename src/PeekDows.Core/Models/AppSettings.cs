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

    /// <summary>
    /// Layout mode: Grid (default, ClassicPeekGrid) | Halves | Thirds.
    /// JSON name is "layoutMode" (camelCase UI contract).
    /// </summary>
    [JsonPropertyName("layoutMode")]
    public string LayoutMode { get; set; } = "Grid";

    public CustomZoneLayout CustomZones { get; set; } = new();
    public Dictionary<string, MonitorLayoutProfile> MonitorProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When true (default), the focus-hint overlay shows per-app icons.
    /// JSON name is "overlayShowIcons" (camelCase UI contract).
    /// </summary>
    [JsonPropertyName("overlayShowIcons")]
    public bool OverlayShowAppIcons { get; set; } = true;

    [JsonPropertyName("soundEnabled")]
    public bool ArrangeSoundEnabled { get; set; } = false;

    [JsonPropertyName("soundName")]
    public string ArrangeSoundName { get; set; } = "chime-soft";

    /// <summary>
    /// Auto-off duration in total minutes (0 = stays on). The legacy
    /// "soundAutoOffHours" key (v1 of this feature) is ignored on load.
    /// JSON name is "soundAutoOffMinutes" (camelCase UI contract).
    /// </summary>
    [JsonPropertyName("soundAutoOffMinutes")]
    public int ArrangeSoundAutoOffMinutes { get; set; } = 0;

    public WindowSizePreset WindowSizePreset { get; set; } = WindowSizePreset.Small;

    public List<string> IgnoredProcesses { get; set; } = new()
    {
        "PeekDows.exe",
        "SystemSettings.exe"
    };

    public List<string> IgnoredClasses { get; set; } = new();

    /// <summary>
    /// Per-app rules (pin to a Grid slot, or ignore). Empty by default.
    /// JSON name is "appRules" (camelCase UI contract).
    /// </summary>
    [JsonPropertyName("appRules")]
    public List<AppRule> AppRules { get; set; } = new();

    public Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        { "arrangeNow", "Ctrl+Alt+Space" },
        { "pauseResume", "Ctrl+Alt+P" },
        { "assignSlotA", "Ctrl+Alt+1" },
        { "assignSlotB", "Ctrl+Alt+2" },
        { "assignSlotC", "Ctrl+Alt+3" },
        { "assignSlotD", "Ctrl+Alt+4" }
    };

    /// <summary>Copies the collections editable by the settings windows.</summary>
    public AppSettings CopyForSettingsEdit()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.IgnoredProcesses = new List<string>(IgnoredProcesses);
        copy.IgnoredClasses = new List<string>(IgnoredClasses);
        copy.Hotkeys = new Dictionary<string, string>(Hotkeys, Hotkeys.Comparer);
        copy.AppRules = AppRules.Select(rule => new AppRule
        {
            Process = rule.Process,
            Action = rule.Action,
            Slot = rule.Slot,
        }).ToList();
        return copy;
    }
}
