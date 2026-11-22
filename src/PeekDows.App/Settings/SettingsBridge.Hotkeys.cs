using System.Collections.Generic;
using PeekDows.Core.Models;

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

    private static string? ApplyHotkeysDraft(SettingsDraft draft, AppSettings next, out bool changed)
    {
        changed = false;
        if (draft.Hotkeys is null) return null;

        foreach (var (name, gesture) in draft.Hotkeys)
        {
            if (!PeekDowsAppContext.DefaultHotkeys.TryGetValue(name, out var defaultGesture)) continue;
            if (!HotkeyParser.TryParse(gesture, out _, out _)) return "bad-gesture";

            var current = next.Hotkeys.GetValueOrDefault(name, defaultGesture);
            if (HotkeyParser.AreEquivalent(current, gesture)) continue;
            next.Hotkeys[name] = gesture;
            changed = true;
        }

        return null;
    }
}
