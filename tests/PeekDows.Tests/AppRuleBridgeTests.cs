using System.IO;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public sealed class AppRuleBridgeTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory().FullName;
    private readonly SettingsService _service;
    private readonly SettingsBridgeTests.FakeBridgeController _controller;
    private readonly SettingsBridge _bridge;

    public AppRuleBridgeTests()
    {
        _service = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        _controller = new SettingsBridgeTests.FakeBridgeController(_service.Load(), _service);
        _bridge = new SettingsBridge(_controller);
    }

    public void Dispose()
    {
        _controller.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private List<AppRule> AppRules
    {
        get => _controller.CurrentSettings.AppRules;
        set => _controller.CurrentSettings.AppRules = value;
    }

    private bool Apply(string message)
    {
        int before = _controller.CommitCount;
        _bridge.HandleMessage(message);
        return _controller.CommitCount != before;
    }

    private static JsonElement SnapshotData(string snapshot)
    {
        using var doc = JsonDocument.Parse(snapshot);
        return doc.RootElement.GetProperty("data").Clone();
    }

    // ----- snapshot -----

    [Fact]
    public void Snapshot_ExposesAppRules_DefaultEmpty()
    {

        var data = SnapshotData(_bridge.BuildSnapshotMessage());

        Assert.Equal(JsonValueKind.Array, data.GetProperty("appRules").ValueKind);
        Assert.Equal(0, data.GetProperty("appRules").GetArrayLength());
    }

    [Fact]
    public void Snapshot_ExposesAppRules_WithEntries()
    {
        AppRules = new List<AppRule>
        {
            new() { Process = "notepad.exe", Action = "pin", Slot = "A" },
            new() { Process = "gamebar", Action = "ignore", Slot = "" },
        };

        var data = SnapshotData(_bridge.BuildSnapshotMessage());
        var arr = data.GetProperty("appRules");

        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("notepad.exe", arr[0].GetProperty("process").GetString());
        Assert.Equal("pin", arr[0].GetProperty("action").GetString());
        Assert.Equal("A", arr[0].GetProperty("slot").GetString());
        Assert.Equal("ignore", arr[1].GetProperty("action").GetString());
    }

    [Fact]
    public void Snapshot_UsesCamelCaseKeys()
    {
        AppRules = new List<AppRule> { new() { Process = "a.exe", Action = "pin", Slot = "A" } };

        var snapshot = _bridge.BuildSnapshotMessage();

        Assert.Contains("\"appRules\"", snapshot);
        Assert.Contains("\"process\"", snapshot);
        Assert.Contains("\"action\"", snapshot);
        Assert.Contains("\"slot\"", snapshot);
        Assert.DoesNotContain("AppRules", snapshot);
        Assert.DoesNotContain("\"Process\"", snapshot);
    }

    // ----- apply : validation -----

    [Fact]
    public void Apply_ValidPinAndIgnore_PersistAndNotify()
    {
        var draft = """{"type":"apply","data":{"appRules":[{"process":"notepad.exe","action":"pin","slot":"A"},{"process":"gamebar","action":"ignore"}]}}""";

        var changed = Apply(draft);

        Assert.True(changed);
        Assert.Equal(2, AppRules.Count);
        Assert.True(AppRuleService.AreRulesEqual(AppRules, _service.Load().AppRules));
        Assert.Equal(1, _controller.CommitCount);
    }

    [Fact]
    public void Apply_EmptyProcess_Rejected()
    {
        var draft = """{"type":"apply","data":{"appRules":[{"process":"","action":"pin","slot":"A"},{"process":"notepad.exe","action":"pin","slot":"A"}]}}""";

        Apply(draft);

        Assert.Single(AppRules);
        Assert.Equal("notepad.exe", AppRules[0].Process);
    }

    [Fact]
    public void Apply_UnknownAction_Rejected()
    {
        var draft = """{"type":"apply","data":{"appRules":[{"process":"notepad.exe","action":"float","slot":"A"},{"process":"calc.exe","action":"pin","slot":"B"}]}}""";

        Apply(draft);

        Assert.Single(AppRules);
        Assert.Equal("calc.exe", AppRules[0].Process);
    }

    [Fact]
    public void Apply_InvalidSlotForPin_Rejected()
    {
        var draft = """{"type":"apply","data":{"appRules":[{"process":"a.exe","action":"pin","slot":"Z"},{"process":"b.exe","action":"pin","slot":""},{"process":"c.exe","action":"pin","slot":"C"}]}}""";

        Apply(draft);

        Assert.Single(AppRules);
        Assert.Equal("c.exe", AppRules[0].Process);
    }

    [Fact]
    public void Apply_IgnoreWithoutSlot_Accepted()
    {
        // Slot omis (cas UI "ignore sans slot") + slot vide explicite.
        var draft = """{"type":"apply","data":{"appRules":[{"process":"gamebar","action":"ignore"},{"process":"overlay","action":"ignore","slot":""}]}}""";

        var changed = Apply(draft);

        Assert.True(changed);
        Assert.Equal(2, AppRules.Count);
        Assert.All(AppRules, r => Assert.Equal("ignore", r.Action));
        Assert.Equal(1, _controller.CommitCount);
    }

    [Fact]
    public void Apply_MalformedAppRules_NoChangeNoNotify()
    {
        var draft = """{"type":"apply","data":{"appRules":"not-an-array"}}""";

        var changed = Apply(draft);

        Assert.False(changed);
        Assert.Empty(AppRules);
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_ActionIsCaseInsensitive()
    {
        var draft = """{"type":"apply","data":{"appRules":[{"process":"a.exe","action":"PIN","slot":"b"}]}}""";

        Apply(draft);

        Assert.Single(AppRules);
        Assert.Equal("pin", AppRules[0].Action);
        Assert.Equal("B", AppRules[0].Slot);
    }

    // ----- apply : dirty tracking -----

    [Fact]
    public void Apply_MissingAppRules_Untouched_NoNotify()
    {
        var draft = """{"type":"apply","data":{"preset":"Small"}}""";

        var changed = Apply(draft);

        Assert.False(changed);
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_SameRules_DoesNotNotify()
    {
        AppRules = new List<AppRule> { new() { Process = "notepad.exe", Action = "pin", Slot = "A" } };
        var draft = """{"type":"apply","data":{"appRules":[{"process":"NOTEPAD.exe","action":"pin","slot":"a"}]}}""";

        var changed = Apply(draft);

        Assert.False(changed);
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_DifferentRules_Notifies()
    {
        AppRules = new List<AppRule> { new() { Process = "notepad.exe", Action = "pin", Slot = "A" } };
        var draft = """{"type":"apply","data":{"appRules":[{"process":"notepad.exe","action":"pin","slot":"B"}]}}""";

        var changed = Apply(draft);

        Assert.True(changed);
        Assert.Equal("B", AppRules[0].Slot);
        Assert.Equal(1, _controller.CommitCount);
    }

    [Fact]
    public void Snapshot_Apply_RoundTrip_IsStable()
    {
        AppRules = new List<AppRule>
        {
            new() { Process = "notepad.exe", Action = "pin", Slot = "A" },
            new() { Process = "gamebar", Action = "ignore", Slot = "" },
        };

        // Le snapshot sérialisé re-appliqué à un store vide redonne les mêmes règles
        // sans second notify (symétrie snapshot ⇄ apply, base du dirty tracking JS).
        var snapshot = _bridge.BuildSnapshotMessage();
        var service = new SettingsService(Path.Combine(_tempDir, "fresh.json"));
        using var fresh = new SettingsBridgeTests.FakeBridgeController(service.Load(), service);
        var freshBridge = new SettingsBridge(fresh);
        var data = SnapshotData(snapshot).GetProperty("appRules").GetRawText();
        var draft = "{\"type\":\"apply\",\"data\":{\"appRules\":" + data + "}}";

        freshBridge.HandleMessage(draft);
        freshBridge.HandleMessage(draft);

        Assert.Equal(1, fresh.CommitCount);
        Assert.True(AppRuleService.AreRulesEqual(AppRules, fresh.CurrentSettings.AppRules));
        Assert.True(AppRuleService.AreRulesEqual(AppRules, service.Load().AppRules));
    }
}
