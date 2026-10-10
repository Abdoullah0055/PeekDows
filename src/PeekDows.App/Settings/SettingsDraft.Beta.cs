namespace PeekDows.App.Settings;

/// <summary>
/// Beta draft keys (Agent C). Nullable = "untouched", camelCase JSON contract:
/// focusHintMode (string, Off|Overlay), soundEnabled (bool),
/// soundName (string), soundAutoOffHours (int, >=0).
/// </summary>
public sealed partial class SettingsDraft
{
    public string? FocusHintMode { get; set; }
    public bool? SoundEnabled { get; set; }
    public string? SoundName { get; set; }
    public int? SoundAutoOffHours { get; set; }
}
