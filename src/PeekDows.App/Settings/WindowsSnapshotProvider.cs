using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>Construit les lignes Windows depuis discovery+classifier. Lecture-seule, cap 200.</summary>
public static class WindowsSnapshotProvider
{
    public static IReadOnlyList<WindowRow> Build(
        WindowDiscoveryService discovery,
        WindowClassifier classifier,
        int cap = 200)
    {
        var raws = discovery.GetTopLevelWindows();
        var list = new List<WindowRow>(raws.Count);
        foreach (var r in raws.Take(cap))
        {
            bool eligible = classifier.IsEligible(r);
            string reason = eligible ? "" : ReasonFor(r, classifier);
            list.Add(new WindowRow(
                r.Hwnd.ToString(),
                r.Title ?? "",
                r.ProcessName ?? "",
                r.ClassName ?? "",
                eligible, reason));
        }
        return list.OrderBy(x => x.Process).ThenBy(x => x.Title).ToList();
    }

    private static string ReasonFor(Core.Models.RawWindowInfo r, WindowClassifier c)
    {
        if (c.IsIgnoredProcess(r.ProcessName)) return "ignored-process";
        if (c.IsIgnoredClass(r.ClassName)) return "ignored-class";
        if (c.IsSystemWindow(r)) return "system";
        return "ineligible";
    }
}
