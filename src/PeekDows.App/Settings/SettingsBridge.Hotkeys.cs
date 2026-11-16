using System.Collections.Generic;

namespace PeekDows.App.Settings;

public sealed partial class SettingsBridge
{
    partial void AugmentSnapshotHotkeys(Dictionary<string, object?> data)
    {
        var h = _controller.CurrentSettings.Hotkeys;
        h ??= new Dictionary<string, string>();
        data["hotkeys"] = new Dictionary<string, string>
        {
            ["arrangeNow"] = h.TryGetValue("arrangeNow", out var a) ? a : "Ctrl+Alt+Space",
            ["pauseResume"] = h.TryGetValue("pauseResume", out var p) ? p : "Ctrl+Alt+P",
        };
    }

    partial void ApplyHotkeysDraft(SettingsDraft draft, ref string? hotkeyError)
    {
        if (draft.Hotkeys is null) return;
        var changes = new Dictionary<string, string>();
        foreach (var kv in draft.Hotkeys)
        {
            if (kv.Key is not ("arrangeNow" or "pauseResume")) continue;
            var h = _controller.CurrentSettings.Hotkeys;
            var cur = h.TryGetValue(kv.Key, out var c) ? c : PeekDowsAppContext.DefaultHotkeys[kv.Key];
            if (string.Equals(cur, kv.Value, System.StringComparison.OrdinalIgnoreCase)) continue;
            changes.Add(kv.Key, kv.Value);
        }
        if (changes.Count > 0 && !_controller.TryUpdateHotkeys(changes, out var error))
            hotkeyError = error;
    }
}
