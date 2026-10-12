namespace PeekDows.App.Settings;

/// <summary>
/// Beta draft keys (Agent C). Nullable = "untouched", camelCase JSON contract:
/// focusHintMode (string, Off|Overlay), overlayShowIcons (bool),
/// soundEnabled (bool),
/// soundName (string), soundAutoOffMinutes (int total minutes, >=0,
/// 0 = permanent), layoutMode (string, Grid|Halves|Thirds),
/// appRules (array of {process, action, slot}).
/// </summary>
public sealed partial class SettingsDraft
{
    public string? FocusHintMode { get; set; }
    public bool? OverlayShowIcons { get; set; }
    public bool? SoundEnabled { get; set; }
    public string? SoundName { get; set; }
    public int? SoundAutoOffMinutes { get; set; }
    public string? LayoutMode { get; set; }
    public System.Collections.Generic.List<Core.Models.AppRule>? AppRules { get; set; }
}
