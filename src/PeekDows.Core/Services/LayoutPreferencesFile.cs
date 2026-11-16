using System.Text.Json;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public sealed record LayoutPreferencesFile(int Version, string LayoutMode, CustomZoneLayout CustomZones,
    List<AppRule> AppRules, Dictionary<string, MonitorLayoutProfile> MonitorProfiles, Dictionary<string, string> Hotkeys,
    WindowSizePreset Preset = WindowSizePreset.Small)
{
    public const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static LayoutPreferencesFile Capture(AppSettings settings) => new(CurrentVersion,
        MonitorLayoutSettings.NormalizeMode(settings.LayoutMode), settings.CustomZones ?? new(),
        AppRuleService.CleanRules(settings.AppRules), settings.MonitorProfiles ?? new(),
        new Dictionary<string, string>(settings.Hotkeys), settings.WindowSizePreset);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static LayoutPreferencesFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 1_048_576) throw new InvalidDataException("Settings file exceeds 1 MB.");
        var data = JsonSerializer.Deserialize<LayoutPreferencesFile>(stream)
            ?? throw new InvalidDataException("Settings file is empty.");
        if (data.Version != CurrentVersion)
            throw new InvalidDataException("Unsupported layout settings file version.");
        if (data.LayoutMode is null || data.CustomZones is null || data.AppRules is null ||
            data.MonitorProfiles is null || data.Hotkeys is null)
            throw new InvalidDataException("Settings file is incomplete.");
        var profiles = new Dictionary<string, MonitorLayoutProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, profile) in data.MonitorProfiles)
        {
            if (string.IsNullOrWhiteSpace(id) || profile is null || profile.Mode is null ||
                profile.Rules is null || !profiles.TryAdd(id, profile with
                {
                    Mode = MonitorLayoutSettings.NormalizeMode(profile.Mode),
                    Rules = AppRuleService.CleanRules(profile.Rules),
                    Zones = profile.Zones is null ? null : Normalize(profile.Zones)
                }))
                throw new InvalidDataException("Settings file contains an invalid screen profile.");
        }
        return data with
        {
            LayoutMode = MonitorLayoutSettings.NormalizeMode(data.LayoutMode),
            CustomZones = Normalize(data.CustomZones),
            AppRules = AppRuleService.CleanRules(data.AppRules),
            MonitorProfiles = profiles,
            Preset = Enum.IsDefined(data.Preset) ? data.Preset : WindowSizePreset.Small
        };
    }

    private static CustomZoneLayout Normalize(CustomZoneLayout zones) => zones with
    {
        WidthPercent = Math.Clamp(zones.WidthPercent, 20, 80),
        HeightPercent = Math.Clamp(zones.HeightPercent, 20, 80)
    };
}
