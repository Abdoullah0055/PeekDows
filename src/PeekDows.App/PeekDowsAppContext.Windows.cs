using PeekDows.App.Settings;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App;

public partial class PeekDowsAppContext
{
    public IReadOnlyList<WindowRow> GetWindowsSnapshot()
        => WindowsSnapshotProvider.Build(_discoveryService, _classifier, 200,
            CreateWindowsPlacementCheck(_settings, _multiMonitorLayoutService.ResolveWindowMonitor, _loneWindowMaximizePolicy));

    internal static Func<RawWindowInfo, (string? Reason, bool HasProfileRule)> CreateWindowsPlacementCheck(
        AppSettings settings, Func<IntPtr, MonitorInfo> resolveMonitor, LoneWindowMaximizePolicy maximizePolicy)
    {
        var rulesByMonitor = new Dictionary<IntPtr, MonitorLayoutSettings.EffectiveAppRules>();
        return window =>
        {
            var monitor = resolveMonitor(window.Hwnd);
            if (monitor.IsFallback) return ("monitor-unavailable", false);
            if (!rulesByMonitor.TryGetValue(monitor.Handle, out var rules))
                rulesByMonitor[monitor.Handle] = rules = MonitorLayoutSettings.ResolveRules(settings, monitor);
            var (rule, hasProfileRule) = rules.ForProcess(window.ProcessName);
            if (hasProfileRule && AppRuleService.IsIgnoreAction(rule?.Action))
                return ("ignored-by-profile", true);
            if (!CanRepositionWindow(window.Hwnd, window.IsMaximized, settings, maximizePolicy))
                return ("maximized", hasProfileRule);
            if (AppRuleService.IsIgnoreAction(rule?.Action))
                return ("ignored-by-rule", false);
            return (null, hasProfileRule);
        };
    }

    private static bool CanRepositionWindow(IntPtr hwnd, bool isMaximized, AppSettings settings,
        LoneWindowMaximizePolicy maximizePolicy)
        => !isMaximized || settings.AllowRepositionMaximizedWindows || maximizePolicy.WasMaximizedByPeekDows(hwnd);
}
