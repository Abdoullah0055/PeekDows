using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.App.Settings;

namespace PeekDows.Tests;

public sealed class AuditFeaturesTests
{
    [Theory]
    [InlineData("Ctrl+Alt+P", "Alt+Control+p", true)]
    [InlineData("Ctrl+Alt+P", "Ctrl+Alt+O", false)]
    public void ImportedShortcutsCanRecognizeUnchangedCombinations(string existing, string incoming, bool unchanged)
    {
        Assert.Equal(unchanged, HotkeyParser.AreEquivalent(existing, incoming));
    }

    [Fact]
    public void CustomZonesCoverAnOddSizedWorkAreaWithNoGaps()
    {
        var area = new Rect(-1001, 23, 1001, 701);
        var zones = LayoutEngine.CalculateCustomZones(area, new(60, true, 40));
        Assert.Equal(new Rect(-1001, 23, 600, 701), zones[0]);
        Assert.Equal(new Rect(-401, 23, 401, 280), zones[1]);
        Assert.Equal(new Rect(-401, 303, 401, 421), zones[2]);
    }

    [Fact]
    public void ProfileOnlyOverridesRulesForItsOwnMonitor()
    {
        var settings = new AppSettings
        {
            LayoutMode = "Thirds",
            AppRules = [new() { Process = "editor.exe", Action = "ignore" }],
            MonitorProfiles = new() { ["test-monitor"] = new("Halves",
                [new() { Process = "editor.exe", Action = "pin", Slot = "A" }]) }
        };
        var matched = MonitorLayoutSettings.Resolve(settings, new() { ProfileId = "TEST-monitor" });
        Assert.Equal("Halves", matched.Mode);
        Assert.Equal("pin", Assert.Single(matched.Rules).Action);

        var unknown = MonitorLayoutSettings.Resolve(settings, new() { ProfileId = "other" });
        Assert.Equal("Thirds", unknown.Mode);
        Assert.Equal("ignore", Assert.Single(unknown.Rules).Action);
    }

    [Fact]
    public void VersionedExportRoundTripsZonesProfilesAndShortcuts()
    {
        var settings = new AppSettings
        {
            LayoutMode = "Custom",
            CustomZones = new(65, true, 45),
            MonitorProfiles = new() { ["dock"] = new("Thirds",
                [new() { Process = "editor.exe", Action = "ignore" }]) },
            Hotkeys = new() { ["arrangeNow"] = "Ctrl+Shift+F8" }
        };
        var file = Path.GetTempFileName();
        try
        {
            var exported = LayoutPreferencesFile.Capture(settings);
            File.WriteAllText(file, exported.ToJson());
            var imported = LayoutPreferencesFile.Read(file);
            Assert.Equal(exported.ToJson(), imported.ToJson());
            Assert.Equal(settings.CustomZones, imported.CustomZones);
            Assert.Equal(settings.Hotkeys["arrangeNow"], imported.Hotkeys["arrangeNow"]);
            Assert.Equal("ignore", Assert.Single(imported.MonitorProfiles["DOCK"].Rules).Action);

            File.WriteAllText(file, (exported with { Version = 99 }).ToJson());
            Assert.Throws<InvalidDataException>(() => LayoutPreferencesFile.Read(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ProfileCanIgnoreAWindowWhileDefaultLayoutOnOtherDisplayStillPlacesIt()
    {
        var area = new Rect(0, 0, 1000, 800);
        var otherArea = area with { Left = 1000 };
        var left = new MonitorInfo { Handle = (IntPtr)1, ProfileId = "left", WorkArea = area };
        var right = new MonitorInfo { Handle = (IntPtr)2, ProfileId = "right", WorkArea = otherArea };
        var settings = new AppSettings
        {
            LayoutMode = "Thirds",
            MonitorProfiles = new() { ["left"] = new("Custom",
                [new() { Process = "editor.exe", Action = "ignore" }], new(65)) }
        };
        var windows = new[]
        {
            new ManagedWindow { Hwnd = (IntPtr)1, ProcessName = "editor.exe" },
            new ManagedWindow { Hwnd = (IntPtr)2, ProcessName = "notes.exe" },
            new ManagedWindow { Hwnd = (IntPtr)3, ProcessName = "editor.exe" }
        };
        var layout = new MultiMonitorLayoutService(new MonitorResolver(left, right), new(), new LoneWindowMaximizePolicy());
        var placements = layout.CalculatePlacementsByMonitor(windows, settings);
        Assert.DoesNotContain(placements, item => item.Hwnd == (IntPtr)1);
        Assert.Equal(new Rect(0, 0, 650, 800), placements.Single(item => item.Hwnd == (IntPtr)2).TargetRect);
        Assert.Equal(PlacementKind.Maximize, placements.Single(item => item.Hwnd == (IntPtr)3).Kind);
    }

    private sealed class MonitorResolver(MonitorInfo left, MonitorInfo right) : IMonitorResolver
    {
        public MonitorInfo GetMonitorForWindow(IntPtr hwnd) => hwnd.ToInt32() <= 2 ? left : right;
        public MonitorInfo GetPrimaryMonitor() => left;
    }
}
