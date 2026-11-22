using System;
using System.Text.Json;
using PeekDows.App.Settings;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Agent C (Beta page): snapshot exposes focusHintMode only (default "Overlay"),
/// apply persists Off|Overlay (case-insensitive) and ignores anything else.
/// Legacy values (Both, Spotlight) are invalid bridge-side and ignored — the V5
/// Core migration normalizes them at load.
/// </summary>
public sealed class SettingsBridgeBetaTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settingsService;
    private AppSettings Settings => _controller.CurrentSettings;
    private readonly SettingsBridgeTests.FakeBridgeController _controller;
    private readonly SettingsBridge _bridge;

    public SettingsBridgeBetaTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(System.IO.Path.Combine(_tempDir, "settings.json"));
        _controller = new SettingsBridgeTests.FakeBridgeController(_settingsService.Load(), _settingsService);
        _bridge = new SettingsBridge(_controller, appVersionProvider: () => "9.9.9");
    }

    public void Dispose()
    {
        _controller.Dispose();
        if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void SoundImport_ReturnsUpdatedLibraryWithoutASnapshot()
    {
        _controller.PickedSound = "new.wav";
        _controller.UserSounds = ["old.wav", "new.wav"];

        var reply = _bridge.HandleMessage("""{"type":"pickSoundFile"}""");
        using var document = JsonDocument.Parse(Assert.IsType<string>(reply));
        var message = document.RootElement;

        Assert.Equal("soundFile", message.GetProperty("type").GetString());
        Assert.Equal("new.wav", message.GetProperty("name").GetString());
        Assert.Equal(_controller.UserSounds, message.GetProperty("userSounds").Deserialize<string[]>());
        Assert.False(message.TryGetProperty("data", out _));
    }

    [Fact]
    public void SoundImport_CancelDoesNotRefreshSettings()
    {
        Assert.Null(_bridge.HandleMessage("""{"type":"pickSoundFile"}"""));
    }

    [Fact]
    public void Snapshot_NullHintMode_FallsBackToOverlay()
    {
        Settings.FocusHintMode = null!;

        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("Overlay", data.GetProperty("focusHintMode").GetString());
    }

    [Fact]
    public void Snapshot_ReflectsMutatedHintMode()
    {
        Settings.FocusHintMode = "Off";

        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("Off", data.GetProperty("focusHintMode").GetString());
    }

    [Fact]
    public void Snapshot_OmitsRemovedBetaKeys()
    {
        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.True(data.TryGetProperty("focusHintMode", out _));
        Assert.True(data.TryGetProperty("overlayShowIcons", out _));
        Assert.False(data.TryGetProperty("spotlightOpacity", out _));
    }

    [Fact]
    public void Apply_OverlayToOff_PersistsNotifiesAndSnapshots()
    {
        Settings.FocusHintMode = "Overlay";

        var response = _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Off"}}""");

        Assert.Equal("Off", Settings.FocusHintMode);
        Assert.Equal("Off", _settingsService.Load().FocusHintMode);
        Assert.Equal(1, _controller.CommitCount);

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Off", doc.RootElement.GetProperty("data").GetProperty("focusHintMode").GetString());
    }

    [Fact]
    public void Apply_OffToOverlay_Persists()
    {
        Settings.FocusHintMode = "Off";

        _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Overlay"}}""");

        Assert.Equal("Overlay", Settings.FocusHintMode);
        Assert.Equal("Overlay", _settingsService.Load().FocusHintMode);
        Assert.Equal(1, _controller.CommitCount);
    }

    [Theory]
    [InlineData("off", "Off")]
    [InlineData("oFf", "Off")]
    [InlineData("overlay", "Overlay")]
    [InlineData("OVERLAY", "Overlay")]
    public void Apply_HintMode_IsCaseInsensitive(string input, string expected)
    {
        Settings.FocusHintMode = expected == "Off" ? "Overlay" : "Off";

        _bridge.HandleMessage("{\"type\":\"apply\",\"data\":{\"focusHintMode\":\"" + input + "\"}}");

        Assert.Equal(expected, Settings.FocusHintMode);
    }

    [Fact]
    public void Apply_InvalidHintMode_Ignored()
    {
        Settings.FocusHintMode = "Overlay";

        var response = _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Laser"}}""");

        Assert.Equal("Overlay", Settings.FocusHintMode);
        Assert.Equal(0, _controller.CommitCount);
        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("Both")]
    [InlineData("Spotlight")]
    [InlineData("both")]
    [InlineData("spotlight")]
    public void Apply_LegacyHintMode_IgnoredByBridge(string legacy)
    {
        // The bridge only normalizes Off|Overlay. Legacy values (Both, Spotlight)
        // are invalid here and ignored; the V5 Core migration normalizes them
        // at load instead.
        Settings.FocusHintMode = "Overlay";

        _bridge.HandleMessage("{\"type\":\"apply\",\"data\":{\"focusHintMode\":\"" + legacy + "\"}}");

        Assert.Equal("Overlay", Settings.FocusHintMode);
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_SameHintMode_DoesNotNotify()
    {
        Settings.FocusHintMode = "Overlay";

        _bridge.HandleMessage("""{"type":"apply","data":{"focusHintMode":"Overlay"}}""");

        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_RemovedBetaKeys_AreIgnored()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"spotlightOpacity":60}}""");

        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Snapshot_OverlayShowIcons_DefaultTrue()
    {
        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.True(data.GetProperty("overlayShowIcons").GetBoolean());
    }

    [Fact]
    public void Apply_OverlayShowIconsFalse_PersistsNotifiesAndSnapshots()
    {
        Assert.True(Settings.OverlayShowAppIcons);

        var response = _bridge.HandleMessage("""{"type":"apply","data":{"overlayShowIcons":false}}""");

        Assert.False(Settings.OverlayShowAppIcons);
        Assert.False(_settingsService.Load().OverlayShowAppIcons);
        Assert.Equal(1, _controller.CommitCount);

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("overlayShowIcons").GetBoolean());
    }

    [Fact]
    public void Apply_SameOverlayShowIcons_DoesNotNotify()
    {
        Settings.OverlayShowAppIcons = true;

        _bridge.HandleMessage("""{"type":"apply","data":{"overlayShowIcons":true}}""");

        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Snapshot_SoundDefaults_Exposed()
    {
        using var doc = JsonDocument.Parse(_bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");

        Assert.False(data.GetProperty("soundEnabled").GetBoolean());
        Assert.Equal("chime-soft", data.GetProperty("soundName").GetString());
        Assert.Equal(0, data.GetProperty("soundAutoOffMinutes").GetInt32());
        Assert.Equal(0, data.GetProperty("userSounds").GetArrayLength());
    }

    [Fact]
    public void Apply_SoundEnabled_TogglesPersistsAndSnapshots()
    {
        Assert.False(Settings.ArrangeSoundEnabled);

        var response = _bridge.HandleMessage("""{"type":"apply","data":{"soundEnabled":true}}""");

        Assert.True(Settings.ArrangeSoundEnabled);
        Assert.True(_settingsService.Load().ArrangeSoundEnabled);

        using var doc = JsonDocument.Parse(response!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("soundEnabled").GetBoolean());
    }

    [Fact]
    public void Apply_SoundName_PersistsAndNotifies()
    {
        var response = _bridge.HandleMessage("""{"type":"apply","data":{"soundName":"pop-minimal"}}""");

        Assert.Equal("pop-minimal", Settings.ArrangeSoundName);
        Assert.Equal("pop-minimal", _settingsService.Load().ArrangeSoundName);
        Assert.Equal(1, _controller.CommitCount);

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal("pop-minimal", doc.RootElement.GetProperty("data").GetProperty("soundName").GetString());
    }

    [Fact]
    public void Apply_SoundAutoOffMinutes_PersistsAndNotifies()
    {
        _bridge.HandleMessage("""{"type":"apply","data":{"soundAutoOffMinutes":90}}""");

        Assert.Equal(90, Settings.ArrangeSoundAutoOffMinutes);
        Assert.Equal(90, _settingsService.Load().ArrangeSoundAutoOffMinutes);
        Assert.Equal(1, _controller.CommitCount);
    }

    [Fact]
    public void Apply_NegativeSoundAutoOffMinutes_Ignored()
    {
        Settings.ArrangeSoundAutoOffMinutes = 0;

        _bridge.HandleMessage("""{"type":"apply","data":{"soundAutoOffMinutes":-2}}""");

        Assert.Equal(0, Settings.ArrangeSoundAutoOffMinutes);
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_EmptySoundName_Ignored()
    {
        Settings.ArrangeSoundName = "chime-soft";

        _bridge.HandleMessage("""{"type":"apply","data":{"soundName":""}}""");

        Assert.Equal("chime-soft", Settings.ArrangeSoundName);
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_SameSoundValues_DoNotNotify()
    {
        Settings.ArrangeSoundName = "chime-soft";
        Settings.ArrangeSoundAutoOffMinutes = 0;

        _bridge.HandleMessage("""{"type":"apply","data":{"soundName":"chime-soft","soundAutoOffMinutes":0}}""");

        Assert.Equal(0, _controller.CommitCount);
    }

}
