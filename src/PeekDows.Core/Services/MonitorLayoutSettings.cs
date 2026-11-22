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
        var profile = FindProfile(settings, monitor);
        return (NormalizeMode(profile is null ? settings.LayoutMode : profile.Mode),
            ResolveRules(settings, profile).Rules, profile?.Zones ?? settings.CustomZones ?? new());
    }

    public static EffectiveAppRules ResolveRules(AppSettings settings, MonitorInfo monitor)
        => ResolveRules(settings, FindProfile(settings, monitor));

    private static MonitorLayoutProfile? FindProfile(AppSettings settings, MonitorInfo monitor)
    {
        if (settings.MonitorProfiles is not { Count: > 0 } profiles || monitor.IsFallback)
            return null;

        var id = monitor.ProfileId;
        if (id.Length == 0 && monitor.DeviceName.Length > 0)
            id = MonitorService.GetProfileId(monitor.DeviceName);
        return profiles.FirstOrDefault(pair =>
            string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static EffectiveAppRules ResolveRules(AppSettings settings, MonitorLayoutProfile? profile)
    {
        var defaults = AppRuleService.CleanRules(settings.AppRules);
        var overrides = AppRuleService.CleanRules(profile?.Rules);
        var overrideNames = overrides.Select(rule => AppRuleService.NormalizeProcess(rule.Process))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        overrides.AddRange(defaults.Where(rule => !overrideNames.Contains(AppRuleService.NormalizeProcess(rule.Process))));
        return new(overrides, overrideNames);
    }

    public sealed class EffectiveAppRules
    {
        private readonly HashSet<string> _overrideNames;

        internal EffectiveAppRules(List<AppRule> rules, HashSet<string> overrideNames)
        {
            Rules = rules;
            _overrideNames = overrideNames;
        }

        internal List<AppRule> Rules { get; }

        public (AppRule? Rule, bool HasProfileRule) ForProcess(string? process)
        {
            var name = AppRuleService.NormalizeProcess(process);
            var rule = Rules.Find(rule =>
                string.Equals(AppRuleService.NormalizeProcess(rule.Process), name, StringComparison.OrdinalIgnoreCase));
            return (rule, _overrideNames.Contains(name));
        }
    }
}
