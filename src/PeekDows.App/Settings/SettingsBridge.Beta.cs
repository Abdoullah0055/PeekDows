using System;
using System.Collections.Generic;

namespace PeekDows.App.Settings;

/// <summary>
/// Beta settings bridge (Agent C): focus hint mode only (Off|Overlay).
/// Backed by Agent 1's AppSettings (FocusHintMode).
/// </summary>
public sealed partial class SettingsBridge
{
    private void AugmentSnapshotBeta(Dictionary<string, object?> data)
    {
        var s = _controller.CurrentSettings;
        data["focusHintMode"] = s.FocusHintMode ?? "Overlay";
        data["preventLayoutSwitch"] = s.PreventLayoutSwitchWhileGesturing;
    }

    private void ApplyBetaDraft(SettingsDraft draft, Core.Models.AppSettings settings, ref bool plainChanged)
    {
        if (draft.FocusHintMode is { } raw
            && TryNormalizeHintMode(raw, out var mode)
            && !string.Equals(settings.FocusHintMode, mode, StringComparison.Ordinal))
        {
            settings.FocusHintMode = mode;
            plainChanged = true;
        }
        if (draft.PreventLayoutSwitch is { } pv && settings.PreventLayoutSwitchWhileGesturing != pv)
        {
            settings.PreventLayoutSwitchWhileGesturing = pv;
            plainChanged = true;
        }
    }

    private static bool TryNormalizeHintMode(string raw, out string mode)
    {
        if (string.Equals(raw, "Off", StringComparison.OrdinalIgnoreCase)) { mode = "Off"; return true; }
        if (string.Equals(raw, "Overlay", StringComparison.OrdinalIgnoreCase)) { mode = "Overlay"; return true; }
        mode = string.Empty;
        return false;
    }
}
