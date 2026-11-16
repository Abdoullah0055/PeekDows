namespace PeekDows.Core.Models;

public sealed record CustomZoneLayout(int WidthPercent = 60, bool SplitSecondary = false, int HeightPercent = 50);

public sealed record MonitorLayoutProfile(string Mode, List<AppRule> Rules, CustomZoneLayout? Zones = null);
