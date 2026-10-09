using System;
using System.IO;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testSettingsPath;
    private readonly SettingsService _settingsService;

    public SettingsServiceTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        _testSettingsPath = Path.Combine(tempDir, "settings.json");
        _settingsService = new SettingsService(_testSettingsPath);
    }

    [Fact]
    public void Load_WhenFileMissing_CreatesAndReturnsDefault()
    {
        var settings = _settingsService.Load();

        Assert.NotNull(settings);
        Assert.True(File.Exists(_testSettingsPath));
        Assert.False(settings.AutoArrange);
    }

    [Fact]
    public void SettingsFilePath_ReturnsConstructorPath()
    {
        Assert.Equal(_testSettingsPath, _settingsService.SettingsFilePath);
    }

    [Fact]
    public void Save_And_Load_PersistsValues()
    {
        var settings = _settingsService.Load();
        settings.AutoArrange = true;
        _settingsService.Save(settings);

        var loadedSettings = _settingsService.Load();

        Assert.True(loadedSettings.AutoArrange);
    }

    [Fact]
    public void Load_WhenFileCorrupt_CreatesBackupAndReturnsDefault()
    {
        File.WriteAllText(_testSettingsPath, "{ CorruptedJSON");

        var settings = _settingsService.Load();

        Assert.NotNull(settings);
        Assert.False(settings.AutoArrange);
        
        var directory = Path.GetDirectoryName(_testSettingsPath);
        Assert.NotNull(directory);
        var files = Directory.GetFiles(directory, "settings.corrupted.*.json");
        Assert.Single(files);
    }

    [Fact]
    public void MigrateIfNeeded_DoesNotReAddRemovedDefaultProcess()
    {
        var settings = _settingsService.Load();
        settings.IgnoredProcesses.RemoveAll(p => p.Equals("PeekDows.exe", StringComparison.OrdinalIgnoreCase));
        _settingsService.Save(settings);

        var reloaded = _settingsService.Load();

        Assert.DoesNotContain("PeekDows.exe", reloaded.IgnoredProcesses);
    }

    [Fact]
    public void MigrateIfNeeded_DoesNotReAddRemovedDefaultClass()
    {
        var settings = _settingsService.Load();
        settings.IgnoredClasses.Clear();
        _settingsService.Save(settings);

        var reloaded = _settingsService.Load();

        Assert.Empty(reloaded.IgnoredClasses);
    }

    [Fact]
    public void MigrateIfNeeded_HandlesNullIgnoredProcesses()
    {
        var settings = _settingsService.Load();
        settings.IgnoredProcesses = null!;

        _settingsService.MigrateIfNeeded(settings);

        Assert.NotNull(settings.IgnoredProcesses);
    }

    [Fact]
    public void MigrateIfNeeded_HandlesNullIgnoredClasses()
    {
        var settings = _settingsService.Load();
        settings.IgnoredClasses = null!;

        _settingsService.MigrateIfNeeded(settings);

        Assert.NotNull(settings.IgnoredClasses);
    }

    [Fact]
    public void MigrateIfNeeded_PreservesCustomUserEntries()
    {
        var settings = _settingsService.Load();
        settings.IgnoredProcesses.Add("MyCustomApp.exe");
        _settingsService.Save(settings);

        var reloaded = _settingsService.Load();

        Assert.Contains("MyCustomApp.exe", reloaded.IgnoredProcesses);
    }

    [Fact]
    public void MigrateIfNeeded_LowersLegacyThreshold80_To50()
    {
        // Existing installs persisted DirectionalFocusThresholdPx = 80 (the original default),
        // which made the shortcut feel heavy. Migration to schema v2 must lower it to the new
        // lighter default (50) so the change actually applies to existing settings.json files.
        var settings = new AppSettings { Version = 1, DirectionalFocusThresholdPx = 80 };
        var migrated = _settingsService.MigrateIfNeeded(settings);
        Assert.Equal(50, migrated.DirectionalFocusThresholdPx);
        Assert.Equal(5, migrated.Version);
    }

    [Fact]
    public void MigrateIfNeeded_PreservesExplicitUserThreshold()
    {
        // A user who customised the threshold (not 80) keeps their explicit choice.
        var settings = new AppSettings { Version = 1, DirectionalFocusThresholdPx = 70 };
        var migrated = _settingsService.MigrateIfNeeded(settings);
        Assert.Equal(70, migrated.DirectionalFocusThresholdPx);
        Assert.Equal(5, migrated.Version);
    }

    [Fact]
    public void MigrateIfNeeded_ClampsInvalidThreshold_To50()
    {
        var settings = new AppSettings { Version = 2, DirectionalFocusThresholdPx = 0 };
        var migrated = _settingsService.MigrateIfNeeded(settings);
        Assert.Equal(50, migrated.DirectionalFocusThresholdPx);
    }

    [Fact]
    public void Save_And_Load_PreservesStartWithWindows()
    {
        var settings = _settingsService.Load();
        settings.StartWithWindows = true;
        _settingsService.Save(settings);

        var loadedSettings = _settingsService.Load();

        Assert.True(loadedSettings.StartWithWindows);
    }

    [Fact]
    public void Save_And_Load_PreservesAutoArrange()
    {
        var settings = _settingsService.Load();
        settings.AutoArrange = true;
        _settingsService.Save(settings);

        var loadedSettings = _settingsService.Load();

        Assert.True(loadedSettings.AutoArrange);
    }

    [Fact]
    public void AppSettings_DefaultAllowRepositionMaximizedWindows_IsFalse()
    {
        var settings = new AppSettings();

        Assert.False(settings.AllowRepositionMaximizedWindows);
    }

    [Fact]
    public void Load_MissingAllowRepositionMaximizedWindows_DefaultsToFalse()
    {
        // Simulates an existing settings.json from before the setting existed: it must not
        // suddenly start repositioning maximized windows.
        File.WriteAllText(_testSettingsPath, "{\"AutoArrange\":true}");

        var settings = _settingsService.Load();

        Assert.False(settings.AllowRepositionMaximizedWindows);
    }

    [Fact]
    public void Save_And_Load_PreservesAllowRepositionMaximizedWindows()
    {
        var settings = _settingsService.Load();
        settings.AllowRepositionMaximizedWindows = true;
        _settingsService.Save(settings);

        var loadedSettings = _settingsService.Load();

        Assert.True(loadedSettings.AllowRepositionMaximizedWindows);
    }

    [Fact]
    public void DefaultWindowSizePreset_IsSmall()
    {
        var settings = new AppSettings();

        Assert.Equal(WindowSizePreset.Small, settings.WindowSizePreset);
    }

    [Fact]
    public void Save_And_Load_PreservesWindowSizePreset()
    {
        var settings = _settingsService.Load();
        settings.WindowSizePreset = WindowSizePreset.Medium;
        _settingsService.Save(settings);

        var loadedSettings = _settingsService.Load();

        Assert.Equal(WindowSizePreset.Medium, loadedSettings.WindowSizePreset);
    }

    [Fact]
    public void Load_MissingWindowSizePreset_DefaultsToSmall()
    {
        File.WriteAllText(_testSettingsPath, "{\"AutoArrange\":true}");

        var settings = _settingsService.Load();

        Assert.Equal(WindowSizePreset.Small, settings.WindowSizePreset);
    }

    [Fact]
    public void AppSettings_DefaultAnimateWindowTransitions_IsTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.AnimateWindowTransitions);
    }

    [Fact]
    public void Save_And_Load_PreservesAnimateWindowTransitions()
    {
        var settings = _settingsService.Load();
        settings.AnimateWindowTransitions = false;
        _settingsService.Save(settings);

        var loadedSettings = _settingsService.Load();

        Assert.False(loadedSettings.AnimateWindowTransitions);
    }

    [Fact]
    public void Load_MissingAnimateWindowTransitions_DefaultsToTrue()
    {
        File.WriteAllText(_testSettingsPath, "{\"AutoArrange\":true}");

        var settings = _settingsService.Load();

        Assert.True(settings.AnimateWindowTransitions);
    }

    [Fact]
    public void MigrateIfNeeded_BumpsVersion2_ToVersion5()
    {
        var settings = new AppSettings { Version = 2 };
        var migrated = _settingsService.MigrateIfNeeded(settings);
        Assert.Equal(5, migrated.Version);
    }

    [Fact]
    public void MigrateIfNeeded_LegacySingleWindowMode_IsDropped()
    {
        // A v2 file carrying SingleWindowMode must deserialize cleanly, and a re-save must
        // not write the retired field back.
        File.WriteAllText(_testSettingsPath, "{\"Version\":2,\"SingleWindowMode\":\"TopLeftSlot\",\"AutoArrange\":true}");

        var settings = _settingsService.Load();
        Assert.True(settings.AutoArrange);

        _settingsService.Save(settings);
        var json = File.ReadAllText(_testSettingsPath);
        Assert.DoesNotContain("SingleWindowMode", json);
    }

    [Fact]
    public void MigrateIfNeeded_V3ToV5_AppliesOverlayDefault()
    {
        // Simulates a real V3 settings.json (no focus-hint keys): migration must apply
        // the Overlay default and bump to V5.
        File.WriteAllText(_testSettingsPath, "{\"Version\":3,\"AutoArrange\":true}");

        var settings = _settingsService.Load();

        Assert.Equal("Overlay", settings.FocusHintMode);
        Assert.Equal(5, settings.Version);
    }

    [Fact]
    public void AppSettings_FocusHintDefaults_AreOverlayVersion5()
    {
        var settings = new AppSettings();

        Assert.Equal("Overlay", settings.FocusHintMode);
        Assert.Equal(5, settings.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("full")]
    public void MigrateIfNeeded_InvalidFocusHintMode_NormalizesToOverlay(string? mode)
    {
        var settings = new AppSettings { Version = 4, FocusHintMode = mode! };

        var migrated = _settingsService.MigrateIfNeeded(settings);

        Assert.Equal("Overlay", migrated.FocusHintMode);
        Assert.Equal(5, migrated.Version);
    }

    [Theory]
    [InlineData("off", "Off")]
    [InlineData("OFF", "Off")]
    [InlineData("overlay", "Overlay")]
    [InlineData("OVERLAY", "Overlay")]
    [InlineData("Off", "Off")]
    [InlineData("Overlay", "Overlay")]
    [InlineData("  Overlay  ", "Overlay")]
    public void MigrateIfNeeded_ValidFocusHintMode_IsPreservedCanonicalized(string input, string expected)
    {
        var settings = new AppSettings { Version = 4, FocusHintMode = input };

        var migrated = _settingsService.MigrateIfNeeded(settings);

        Assert.Equal(expected, migrated.FocusHintMode);
        Assert.Equal(5, migrated.Version);
    }

    [Theory]
    [InlineData("Both", "Overlay")]
    [InlineData("both", "Overlay")]
    [InlineData("BOTH", "Overlay")]
    [InlineData("Spotlight", "Off")]
    [InlineData("spotlight", "Off")]
    [InlineData("SPOTLIGHT", "Off")]
    public void MigrateIfNeeded_LegacyFocusHintMode_IsRemappedToV5(string input, string expected)
    {
        var settings = new AppSettings { Version = 4, FocusHintMode = input };

        var migrated = _settingsService.MigrateIfNeeded(settings);

        Assert.Equal(expected, migrated.FocusHintMode);
        Assert.Equal(5, migrated.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    public void MigrateIfNeeded_UnknownFocusHintMode_DefaultsToOverlay(string? mode)
    {
        var settings = new AppSettings { Version = 4, FocusHintMode = mode! };

        var migrated = _settingsService.MigrateIfNeeded(settings);

        Assert.Equal("Overlay", migrated.FocusHintMode);
        Assert.Equal(5, migrated.Version);
    }

    [Fact]
    public void MigrateIfNeeded_V5AlreadyCurrent_PreservesOffAndOverlay()
    {
        var off = _settingsService.MigrateIfNeeded(new AppSettings { Version = 5, FocusHintMode = "Off" });
        var overlay = _settingsService.MigrateIfNeeded(new AppSettings { Version = 5, FocusHintMode = "Overlay" });

        Assert.Equal("Off", off.FocusHintMode);
        Assert.Equal("Overlay", overlay.FocusHintMode);
        Assert.Equal(5, off.Version);
        Assert.Equal(5, overlay.Version);
    }

    [Fact]
    public void Load_LegacyV4Json_WithSpotlightAndStaleKeys_MigratesToV5()
    {
        // Real V4 file: spotlight mode + retired overlayShowIcons/spotlightOpacity keys.
        // Stale keys are ignored by the deserializer; mode maps Spotlight → Off.
        File.WriteAllText(_testSettingsPath,
            "{\"Version\":4,\"focusHintMode\":\"Spotlight\",\"overlayShowIcons\":false,\"spotlightOpacity\":60}");

        var settings = _settingsService.Load();

        Assert.Equal("Off", settings.FocusHintMode);
        Assert.Equal(5, settings.Version);
    }

    [Fact]
    public void Load_LegacyV4Json_WithBoth_MigratesToOverlay()
    {
        File.WriteAllText(_testSettingsPath, "{\"Version\":4,\"focusHintMode\":\"Both\"}");

        var settings = _settingsService.Load();

        Assert.Equal("Overlay", settings.FocusHintMode);
        Assert.Equal(5, settings.Version);
    }

    [Fact]
    public void Save_And_Load_PreservesFocusHintOverlay()
    {
        var settings = _settingsService.Load();
        settings.FocusHintMode = "Overlay";
        _settingsService.Save(settings);

        var loaded = _settingsService.Load();

        Assert.Equal("Overlay", loaded.FocusHintMode);
    }

    [Fact]
    public void Save_And_Load_PreservesFocusHintOff()
    {
        var settings = _settingsService.Load();
        settings.FocusHintMode = "Off";
        _settingsService.Save(settings);

        var loaded = _settingsService.Load();

        Assert.Equal("Off", loaded.FocusHintMode);
    }

    [Fact]
    public void Save_WritesCamelCaseFocusHintKey_ForUiContract()
    {
        var settings = _settingsService.Load();
        _settingsService.Save(settings);

        var json = File.ReadAllText(_testSettingsPath);

        Assert.Contains("focusHintMode", json);
        Assert.DoesNotContain("overlayShowIcons", json);
        Assert.DoesNotContain("spotlightOpacity", json);
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_testSettingsPath);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }
}
