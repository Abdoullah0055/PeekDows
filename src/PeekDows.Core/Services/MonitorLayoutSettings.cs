using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public static class MonitorLayoutSettings
{
    public static string NormalizeMode(string? mode) => mode?.Trim().ToUpperInvariant() switch
    {
        "HALVES" => "Halves",
        "THIRDS" => "Thirds",
        "CUSTOM" => "Custom",
        _ => "Grid"
    };

    public static (string Mode, List<AppRule> Rules, CustomZoneLayout Zones) Resolve(AppSettings settings, MonitorInfo monitor)
    {
        var rules = AppRuleService.CleanRules(settings.AppRules);
        MonitorLayoutProfile? profile = null;
        if (settings.MonitorProfiles is { Count: > 0 } profiles && !monitor.IsFallback)
        {
            var id = monitor.ProfileId;
            if (id.Length == 0 && monitor.DeviceName.Length > 0)
                id = MonitorService.GetProfileId(monitor.DeviceName);
            profile = profiles.FirstOrDefault(pair =>
                string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase)).Value;
        }
        if (profile is null)
            return (NormalizeMode(settings.LayoutMode), rules, settings.CustomZones ?? new());

        var overrides = AppRuleService.CleanRules(profile.Rules);
        var names = overrides.Select(rule => rule.Process).ToHashSet(StringComparer.OrdinalIgnoreCase);
        overrides.AddRange(rules.Where(rule => !names.Contains(rule.Process)));
        return (NormalizeMode(profile.Mode), overrides, profile.Zones ?? settings.CustomZones ?? new());
    }
}
