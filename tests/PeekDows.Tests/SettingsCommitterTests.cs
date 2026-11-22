using System.IO;
using PeekDows.App;
using PeekDows.App.Hotkeys;
using PeekDows.App.Settings;
using PeekDows.App.Startup;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public sealed class SettingsCommitterTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory().FullName;
    private readonly SimulatedHotkeyApi _native = new();
    private readonly List<string> _errors = new();

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaveFailure_RestoresShortcutAndBothNativeHotkeys(bool initiallyEnabled)
    {
        var original = new byte[] { 0, 1, 253, 254, 255 };
        var startup = CreateStartupService(_tempDir);
        if (initiallyEnabled)
            startup.RestoreShortcut(original);

        var previous = new AppSettings { StartWithWindows = initiallyEnabled };
        var next = previous.CopyForSettingsEdit();
        next.StartWithWindows = !initiallyEnabled;
        next.Hotkeys["arrangeNow"] = "Ctrl+Alt+F8";
        next.Hotkeys["pauseResume"] = "Ctrl+Alt+F9";
        next.LayoutMode = "Halves";
        using var hotkeys = CreateHotkeyService();
        var invalidPath = Directory.CreateDirectory(Path.Combine(_tempDir, "destination")).FullName;
        var service = new SettingsService(invalidPath);
        int saveCount = 0;
        int publishCount = 0;
        int arrangeCount = 0;
        byte[]? shortcutAtWrite = null;
        string? arrangeHotkeyAtWrite = null;
        string? pauseHotkeyAtWrite = null;

        var success = SettingsCommitter.TryCommit(previous, next, startup, hotkeys.TryUpdateGestures,
            settings =>
            {
                saveCount++;
                shortcutAtWrite = startup.CaptureShortcut();
                arrangeHotkeyAtWrite = _native.RegisteredGesture(1);
                pauseHotkeyAtWrite = _native.RegisteredGesture(2);
                service.Save(settings);
            }, _ => publishCount++, () => false, () => arrangeCount++,
            Log, out var error);

        Assert.False(success);
        Assert.Equal("settings-write-failed", error);
        Assert.Equal(["settings-write-failed"], _errors);
        Assert.Equal(1, saveCount);
        Assert.Equal(0, publishCount);
        Assert.Equal(0, arrangeCount);
        Assert.Equal("Ctrl+Alt+F8", arrangeHotkeyAtWrite);
        Assert.Equal("Ctrl+Alt+F9", pauseHotkeyAtWrite);
        Assert.Equal("Ctrl+Alt+Space", _native.RegisteredGesture(1));
        Assert.Equal("Ctrl+Alt+P", _native.RegisteredGesture(2));
        if (initiallyEnabled)
        {
            Assert.Null(shortcutAtWrite);
            Assert.Equal(original, startup.CaptureShortcut());
        }
        else
        {
            Assert.Equal(new byte[] { 17, 23, 35 }, shortcutAtWrite);
            Assert.Null(startup.CaptureShortcut());
        }
    }

    [Fact]
    public void StartupUpdateFailure_AfterPartialShortcutCreation_RollsBackBeforeSaving()
    {
        var startup = CreateStartupService(_tempDir, path =>
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            return false;
        });
        var previous = new AppSettings();
        var next = previous.CopyForSettingsEdit();
        next.StartWithWindows = true;
        next.Hotkeys["arrangeNow"] = "Ctrl+Alt+F8";
        using var hotkeys = CreateHotkeyService();
        var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));

        var success = SettingsCommitter.TryCommit(previous, next, startup, hotkeys.TryUpdateGestures,
            service.Save, _ => Assert.Fail("Unexpected publication"), () => false,
            () => Assert.Fail("Unexpected arrange"), Log, out var error);

        Assert.False(success);
        Assert.Equal("startup-update-failed", error);
        Assert.Null(startup.CaptureShortcut());
        Assert.False(File.Exists(service.SettingsFilePath));
        Assert.Equal("Ctrl+Alt+Space", _native.RegisteredGesture(1));
        Assert.Equal(["startup-update-failed"], _errors);
    }

    [Fact]
    public void StartupCaptureFailure_PreservesExistingState()
    {
        var startup = CreateStartupService(_tempDir);
        var invalidShortcut = Directory.CreateDirectory(Path.Combine(_tempDir, "PeekDows.lnk"));
        var previous = new AppSettings { StartWithWindows = true };
        var next = previous.CopyForSettingsEdit();
        next.StartWithWindows = false;
        next.Hotkeys["arrangeNow"] = "Ctrl+Alt+F8";
        using var hotkeys = CreateHotkeyService();
        var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));

        var success = SettingsCommitter.TryCommit(previous, next, startup, hotkeys.TryUpdateGestures,
            service.Save, _ => Assert.Fail("Unexpected publication"), () => false,
            () => Assert.Fail("Unexpected arrange"), Log, out var error);

        Assert.False(success);
        Assert.Equal("startup-update-failed", error);
        Assert.True(invalidShortcut.Exists);
        Assert.False(File.Exists(service.SettingsFilePath));
        Assert.Equal("Ctrl+Alt+Space", _native.RegisteredGesture(1));
        Assert.Equal(["startup-update-failed"], _errors);
    }

    [Fact]
    public void ConflictOnSecondHotkey_RestoresFirstAndSkipsStartupAndSave()
    {
        var startup = CreateStartupService(_tempDir);
        var previous = new AppSettings();
        var next = previous.CopyForSettingsEdit();
        next.StartWithWindows = true;
        next.Hotkeys["arrangeNow"] = "Ctrl+Alt+F8";
        next.Hotkeys["pauseResume"] = "Ctrl+Alt+F9";
        using var hotkeys = CreateHotkeyService();
        _native.TakeGesture("Ctrl+Alt+F9");
        var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));

        var success = SettingsCommitter.TryCommit(previous, next, startup, hotkeys.TryUpdateGestures,
            service.Save, _ => Assert.Fail("Unexpected publication"), () => false,
            () => Assert.Fail("Unexpected arrange"), Log, out var error);

        Assert.False(success);
        Assert.Equal("hotkey-conflict", error);
        Assert.Null(startup.CaptureShortcut());
        Assert.False(File.Exists(service.SettingsFilePath));
        Assert.Equal("Ctrl+Alt+Space", _native.RegisteredGesture(1));
        Assert.Equal("Ctrl+Alt+P", _native.RegisteredGesture(2));
        Assert.Empty(_errors);
    }

    [Fact]
    public void PublicationFailure_LeavesSavedSettingsAndSuppressesArrange()
    {
        var startup = CreateStartupService(_tempDir);
        var previous = new AppSettings();
        var next = previous.CopyForSettingsEdit();
        next.LayoutMode = "Halves";
        using var hotkeys = CreateHotkeyService();
        var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        int arrangeCount = 0;

        var success = SettingsCommitter.TryCommit(previous, next, startup, hotkeys.TryUpdateGestures,
            service.Save, _ => throw new InvalidOperationException(), () => false,
            () => arrangeCount++, Log, out var error);

        Assert.True(success, error);
        Assert.Equal("Halves", service.Load().LayoutMode);
        Assert.Equal(0, arrangeCount);
        Assert.Single(_errors);
    }

    private HotkeyService CreateHotkeyService()
    {
        var hotkeys = new HotkeyService(_native.Register, _native.Unregister);
        Assert.True(hotkeys.TryUpdateGestures(PeekDowsAppContext.DefaultHotkeys, out var error), error);
        return hotkeys;
    }

    private void Log(string error, Exception _) => _errors.Add(error);

    internal static StartupService CreateStartupService(string folder, Func<string, bool>? createShortcut = null)
    {
        var exe = Path.Combine(folder, "PeekDows-fixture.exe");
        File.WriteAllBytes(exe, []);
        return new StartupService(folder, exe, folder, null, createShortcut ?? (path =>
        {
            File.WriteAllBytes(path, [17, 23, 35]);
            return true;
        }));
    }

    internal sealed class SimulatedHotkeyApi
    {
        private readonly Dictionary<int, (uint Modifiers, uint Key)> _gestures = new();
        private readonly HashSet<(uint, uint)> _occupied = new();

        public bool Register(int id, uint modifiers, uint key)
        {
            var gesture = (modifiers, key);
            if (_occupied.Contains(gesture) || _gestures.ContainsValue(gesture))
                return false;
            return _gestures.TryAdd(id, gesture);
        }

        public bool Unregister(int id) => _gestures.Remove(id);

        public void TakeGesture(string gesture)
        {
            Assert.True(HotkeyParser.TryParse(gesture, out var modifiers, out var key));
            _occupied.Add((modifiers, key));
        }

        public string RegisteredGesture(int id)
        {
            var (modifiers, key) = _gestures[id];
            return HotkeyParser.Format(modifiers, key);
        }
    }
}
