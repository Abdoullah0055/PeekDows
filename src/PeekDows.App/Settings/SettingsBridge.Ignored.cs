using System.Collections.Generic;

namespace PeekDows.App.Settings;

public sealed partial class SettingsBridge
{
    partial void AugmentSnapshotIgnored(Dictionary<string, object?> data)
    {
        var s = _controller.CurrentSettings;
        data["ignoredProcesses"] = s.IgnoredProcesses ?? new List<string>();
        data["ignoredClasses"] = s.IgnoredClasses ?? new List<string>();
    }

    partial void ApplyIgnoredDraft(SettingsDraft draft, Core.Models.AppSettings settings, ref bool changed)
    {
        if (draft.IgnoredProcesses is { } ip)
        {
            var clean = IgnoredListsValidator.Clean(ip);
            if (!clean.SequenceEqual(settings.IgnoredProcesses, System.StringComparer.OrdinalIgnoreCase))
            {
                settings.IgnoredProcesses = clean;
                changed = true;
            }
        }
        if (draft.IgnoredClasses is { } ic)
        {
            var clean = IgnoredListsValidator.Clean(ic);
            if (!clean.SequenceEqual(settings.IgnoredClasses, System.StringComparer.OrdinalIgnoreCase))
            {
                settings.IgnoredClasses = clean;
                changed = true;
            }
        }
    }
}
