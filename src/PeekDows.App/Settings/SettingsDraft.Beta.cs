namespace PeekDows.App.Settings;

/// <summary>
/// Beta draft keys (Agent C). Nullable = "untouched", camelCase JSON contract:
/// focusHintMode (string, Off|Overlay).
/// </summary>
public sealed partial class SettingsDraft
{
    public string? FocusHintMode { get; set; }
}
