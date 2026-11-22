using System.IO;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public sealed class SettingsBridgeHotkeysTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory().FullName;
    private readonly SettingsService _service;
    private readonly SettingsBridgeTests.FakeBridgeController _controller;
    private readonly SettingsBridge _bridge;

    public SettingsBridgeHotkeysTests()
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

    [Fact]
    public void Snapshot_ContainsHotkeyDefaults()
    {
        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var hotkeys = doc.RootElement.GetProperty("data").GetProperty("hotkeys");
        Assert.Equal("Ctrl+Alt+Space", hotkeys.GetProperty("arrangeNow").GetString());
        Assert.Equal("Ctrl+Alt+P", hotkeys.GetProperty("pauseResume").GetString());
    }

    [Fact]
    public void Apply_EquivalentValue_DoesNotCommit()
    {
        var response = _bridge.HandleMessage(
            """{"type":"apply","data":{"hotkeys":{"arrangeNow":"alt+control+space"}}}""");

        Assert.Equal(0, _controller.CommitCount);
        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Ctrl+Alt+Space", _service.Load().Hotkeys["arrangeNow"]);
    }

    [Fact]
    public void Apply_InvalidHotkey_RejectsWholeDraft()
    {
        var response = _bridge.HandleMessage(
            """{"type":"apply","data":{"enabled":false,"hotkeys":{"arrangeNow":"Ctrl+Alt+Conflict"}}}""");

        using var doc = JsonDocument.Parse(response!);
        var root = doc.RootElement;
        Assert.Equal("applied", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("bad-gesture", root.GetProperty("error").GetString());
        Assert.Equal(0, _controller.CommitCount);
        Assert.True(_controller.CurrentSettings.Enabled);
        Assert.Equal("Ctrl+Alt+Space", _service.Load().Hotkeys["arrangeNow"]);
    }

    [Fact]
    public void Apply_UnknownHotkeyKey_LeavesSavedBindingUntouched()
    {
        var response = _bridge.HandleMessage(
            """{"type":"apply","data":{"hotkeys":{"assignSlotA":"Ctrl+Alt+F9"}}}""");

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, _controller.CommitCount);
        Assert.Equal("Ctrl+Alt+1", _service.Load().Hotkeys["assignSlotA"]);
    }
}
