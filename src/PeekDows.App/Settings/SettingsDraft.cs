namespace PeekDows.App.Settings;

/// <summary>
/// Editable draft payload sent by the settings UI on Save. Nullable keys are "untouched".
/// JSON is camelCase; PropertyNameCaseInsensitive accepts either casing.
/// Core keys — extended by partials (Ignored, Hotkeys).
/// </summary>
public sealed partial class SettingsDraft
{
    public bool? Enabled { get; set; }
    public bool? AutoArrange { get; set; }
    public bool? Animate { get; set; }
    public bool? DirectionalFocus { get; set; }
    public bool? StartWithWindows { get; set; }
    public bool? AllowRepositionMaximized { get; set; }
    public string? Preset { get; set; }
    public bool? ArrangeOnStartup { get; set; }
    public bool? ShowTrayNotifications { get; set; }
    public int? ThresholdPx { get; set; }
}
