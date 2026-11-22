using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PeekDows.App;
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
    public void SoundImport_QueuesOnePickerAndPostsTheUpdatedLibrary()
    {
        var work = new Queue<Action>();
        var replies = new List<string>();
        var bridge = new SettingsBridge(_controller, runDialog: work.Enqueue, postAsyncResponse: replies.Add);
        _controller.PickedSound = "new.wav";
        _controller.UserSounds = ["old.wav"];

        Assert.Null(bridge.HandleMessage("""{"type":"pickSoundFile"}"""));
        Assert.Null(bridge.HandleMessage("""{"type":"pickSoundFile"}"""));
        Assert.DoesNotContain(nameof(_controller.PickArrangeSoundFile), _controller.Calls);
        Assert.Empty(replies);
        var pick = Assert.Single(work);
        _controller.UserSounds = ["old.wav", "new.wav"];
        pick();
        Assert.Contains(nameof(_controller.PickArrangeSoundFile), _controller.Calls);
        using var document = JsonDocument.Parse(Assert.Single(replies));
        var message = document.RootElement;

        Assert.Equal("soundFile", message.GetProperty("type").GetString());
        Assert.Equal("new.wav", message.GetProperty("name").GetString());
        Assert.Equal(_controller.UserSounds, message.GetProperty("userSounds").Deserialize<string[]>());
        Assert.False(message.TryGetProperty("data", out _));
    }

    [Fact]
    public void SoundImport_CancelPostsNothingAndReleasesThePicker()
    {
        var work = new Queue<Action>();
        var replies = new List<string>();
        var bridge = new SettingsBridge(_controller, runDialog: work.Enqueue, postAsyncResponse: replies.Add);

        Assert.Null(bridge.HandleMessage("""{"type":"pickSoundFile"}"""));
        work.Dequeue()();
        Assert.Empty(replies);
        Assert.Null(bridge.HandleMessage("""{"type":"pickSoundFile"}"""));
        Assert.Single(work);
    }

    [Fact]
    public void SoundImport_FailurePostsAnAudioErrorAndReleasesThePicker()
    {
        var work = new Queue<Action>();
        var replies = new List<string>();
        var bridge = new SettingsBridge(_controller, runDialog: work.Enqueue, postAsyncResponse: replies.Add);
        _controller.SoundPickError = new IOException("read-failed");

        Assert.Null(bridge.HandleMessage("""{"type":"pickSoundFile"}"""));
        work.Dequeue()();
        using var document = JsonDocument.Parse(Assert.Single(replies));
        Assert.Equal("soundError", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("pick", document.RootElement.GetProperty("operation").GetString());
        Assert.Null(bridge.HandleMessage("""{"type":"pickSoundFile"}"""));
        Assert.Single(work);
    }

    [Fact]
    public void SoundDelete_ReturnsOnlyTheUpdatedLibraryAndPersistentSelection()
    {
        _controller.UserSounds = ["keep.wav"];

        var reply = _bridge.HandleMessage("""{"type":"deleteSoundFile","name":"custom.wav"}""");
        using var document = JsonDocument.Parse(Assert.IsType<string>(reply));
        var message = document.RootElement;

        Assert.Equal("soundFile", message.GetProperty("type").GetString());
        Assert.Equal("custom.wav", message.GetProperty("deletedName").GetString());
        Assert.Equal("chime-soft", message.GetProperty("soundName").GetString());
        Assert.Equal(_controller.UserSounds, message.GetProperty("userSounds").Deserialize<string[]>());
        Assert.False(message.TryGetProperty("data", out _));
    }

    [Fact]
    public void SoundDelete_FailurePostsAnAudioError()
    {
        _controller.SoundDeleteError = new IOException("delete-failed");

        var reply = _bridge.HandleMessage("""{"type":"deleteSoundFile","name":"custom.wav"}""");
        using var document = JsonDocument.Parse(Assert.IsType<string>(reply));
        Assert.Equal("soundError", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("delete", document.RootElement.GetProperty("operation").GetString());
    }

    [Fact]
    public void SoundDelete_SelectedFileIsRestoredWhenFallbackSettingsCannotBeWritten()
    {
        const string name = "selected.wav";
        var file = Path.Combine(_tempDir, name);
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(file, original);
        Settings.ArrangeSoundName = name;
        _settingsService.Save(Settings);
        var before = File.ReadAllBytes(_settingsService.SettingsFilePath);
        var invalidDestination = new SettingsService(Directory.CreateDirectory(Path.Combine(_tempDir, "invalid")).FullName);
        using var controller = new SettingsBridgeTests.FakeBridgeController(Settings, invalidDestination);
        var fallback = Settings.CopyForSettingsEdit();
        fallback.ArrangeSoundName = "chime-soft";

        var exception = Assert.Throws<IOException>(() => PeekDowsAppContext.DeleteSelectedSound(file, () =>
        {
            Assert.False(File.Exists(file));
            Assert.Single(Directory.GetFiles(_tempDir, "*.tmp"));
            if (!controller.TryCommitSettings(fallback, out var error)) throw new IOException(error);
        }, (_, _) => { }));

        Assert.Equal("settings-write-failed", exception.Message);
        Assert.Equal(name, controller.CurrentSettings.ArrangeSoundName);
        Assert.Equal(before, File.ReadAllBytes(_settingsService.SettingsFilePath));
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void SoundDelete_FailedRestoreKeepsOriginalBytesForRecoveryAndReportsFailure()
    {
        var file = Path.Combine(_tempDir, "selected.wav");
        File.WriteAllText(file, "original");

        var exception = Assert.Throws<IOException>(() => PeekDowsAppContext.DeleteSelectedSound(file, () =>
        {
            File.WriteAllText(file, "replacement made after move");
            throw new IOException("settings-write-failed");
        }, (_, _) => { }));

        var recoveryFile = Assert.Single(Directory.GetFiles(_tempDir, "*.tmp"));
        Assert.Equal("original", File.ReadAllText(recoveryFile));
        Assert.Equal("replacement made after move", File.ReadAllText(file));
        Assert.Contains(recoveryFile, exception.Message);
        Assert.Equal(2, Assert.IsType<AggregateException>(exception.InnerException).InnerExceptions.Count);
    }

    [Fact]
    public void SoundDelete_SelectedFileAndTemporaryCopyAreRemovedAfterFallbackPersists()
    {
        var file = Path.Combine(_tempDir, "selected.wav");
        File.WriteAllText(file, "original");
        Settings.ArrangeSoundName = "selected.wav";
        var fallback = Settings.CopyForSettingsEdit();
        fallback.ArrangeSoundName = "chime-soft";

        PeekDowsAppContext.DeleteSelectedSound(file, () =>
        {
            if (!_controller.TryCommitSettings(fallback, out var error)) throw new IOException(error);
        }, (_, _) => { });

        Assert.False(File.Exists(file));
        Assert.Equal("chime-soft", _controller.CurrentSettings.ArrangeSoundName);
        Assert.Equal("chime-soft", _settingsService.Load().ArrangeSoundName);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Theory]
    [InlineData("""{"type":"apply","requestId":42}""", "apply-without-data")]
    [InlineData("""{"type":"apply","requestId":42,"data":null}""", "empty-draft")]
    [InlineData("""{"type":"apply","requestId":42,"data":{"animate":[]}}""", "malformed-draft")]
    public void Apply_RejectsInvalidDraftsWithTheSameRequestId(string request, string error)
    {
        using var document = JsonDocument.Parse(_bridge.HandleMessage(request)!);
        var message = document.RootElement;
        Assert.Equal("applied", message.GetProperty("type").GetString());
        Assert.Equal(42, message.GetProperty("requestId").GetInt64());
        Assert.False(message.GetProperty("ok").GetBoolean());
        Assert.Equal(error, message.GetProperty("error").GetString());
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void Apply_UnexpectedFailureStillReturnsTheRequestId()
    {
        var bridge = new SettingsBridge(_controller, appVersionProvider: () => throw new IOException("snapshot"));
        using var document = JsonDocument.Parse(bridge.HandleMessage("""{"type":"apply","requestId":41,"data":{}}""")!);
        var message = document.RootElement;
        Assert.Equal("applied", message.GetProperty("type").GetString());
        Assert.Equal(41, message.GetProperty("requestId").GetInt64());
        Assert.False(message.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("""{"type":23}""", "missing-type")]
    [InlineData("""{"type":"apply","requestId":"42","data":{"soundEnabled":true}}""", "invalid-request-id")]
    [InlineData("""{"type":"apply","requestId":2.5,"data":{"soundEnabled":true}}""", "invalid-request-id")]
    public void Apply_RejectsNonstringTypesAndNonintegerRequestIds(string request, string code)
    {
        using var document = JsonDocument.Parse(_bridge.HandleMessage(request)!);
        Assert.Equal("error", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void WebViewApply_RequiresARequestId()
    {
        using var document = JsonDocument.Parse(_bridge.HandleWebMessage("""{"type":"apply","data":{"soundEnabled":true}}""")!);
        Assert.Equal("error", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("invalid-request-id", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, _controller.CommitCount);
    }

    [Fact]
    public void WebViewApply_RejectsSelectingADeletedSoundAndKeepsExistingSettings()
    {
        Settings.ArrangeSoundName = "keep.wav";
        _controller.UserSounds = ["keep.wav"];
        using var document = JsonDocument.Parse(
            _bridge.HandleWebMessage("""{"type":"apply","requestId":26,"data":{"soundName":"deleted.wav"}}""")!);
        var reply = document.RootElement;
        Assert.Equal("applied", reply.GetProperty("type").GetString());
        Assert.Equal(26, reply.GetProperty("requestId").GetInt64());
        Assert.False(reply.GetProperty("ok").GetBoolean());
        Assert.Equal("sound-unavailable", reply.GetProperty("error").GetString());
        Assert.Equal("keep.wav", Settings.ArrangeSoundName);
        Assert.Equal(0, _controller.CommitCount);
    }

    [Theory]
    [InlineData("""{"type":"apply","requestId":27,"data":{"animate":false}}""")]
    [InlineData("""{"type":"apply","requestId":27,"data":{"soundName":"removed.wav","animate":false}}""")]
    public void WebViewApply_LeavesAnUnchangedLegacySoundSelectionAlone(string request)
    {
        Settings.ArrangeSoundName = "removed.wav";
        using var document = JsonDocument.Parse(_bridge.HandleWebMessage(request)!);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("removed.wav", Settings.ArrangeSoundName);
        Assert.False(Settings.AnimateWindowTransitions);
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

        var response = _bridge.HandleMessage("""{"type":"apply","requestId":17,"data":{"focusHintMode":"Off"}}""");

        Assert.Equal("Off", Settings.FocusHintMode);
        Assert.Equal("Off", _settingsService.Load().FocusHintMode);
        Assert.Equal(1, _controller.CommitCount);

        using var doc = JsonDocument.Parse(response!);
        Assert.Equal(17, doc.RootElement.GetProperty("requestId").GetInt64());
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
