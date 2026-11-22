using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>Construit les lignes Windows depuis discovery+classifier. Lecture-seule, cap 200.</summary>
public static class WindowsSnapshotProvider
{
    public static IReadOnlyList<WindowRow> Build(
        WindowDiscoveryService discovery,
        WindowClassifier classifier,
        int cap = 200,
        Func<RawWindowInfo, string?>? placementReason = null)
        => Build(discovery, classifier, cap, raw => (placementReason?.Invoke(raw), false));

    internal static IReadOnlyList<WindowRow> Build(
        WindowDiscoveryService discovery,
        WindowClassifier classifier,
        int cap,
        Func<RawWindowInfo, (string? Reason, bool HasProfileRule)> placementCheck)
    {
        var raws = discovery.GetTopLevelWindows();
        var list = new List<WindowRow>(raws.Count);
        foreach (var r in raws.Take(cap))
        {
            var (reason, hasProfileRule) = classifier.IsEligible(r)
                ? placementCheck(r)
                : (ReasonFor(r, classifier), false);
            list.Add(new WindowRow(
                r.Hwnd.ToString(),
                r.Title ?? "",
                r.ProcessName ?? "",
                r.ClassName ?? "",
                reason is null, reason ?? "", hasProfileRule));
        }
        return list.OrderBy(x => x.Process).ThenBy(x => x.Title).ToList();
    }

    private static string ReasonFor(RawWindowInfo r, WindowClassifier c)
    {
        if (c.IsSystemWindow(r)) return "system";
        if (c.IsIgnoredProcess(r.ProcessName)) return "ignored-process";
        if (c.IsIgnoredClass(r.ClassName)) return "ignored-class";
        return "ineligible";
    }
}
