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
        Assert.Equal(2, migrated.Version);
    }

    [Fact]
    public void MigrateIfNeeded_PreservesExplicitUserThreshold()
    {
        // A user who customised the threshold (not 80) keeps their explicit choice.
        var settings = new AppSettings { Version = 1, DirectionalFocusThresholdPx = 70 };
        var migrated = _settingsService.MigrateIfNeeded(settings);
        Assert.Equal(70, migrated.DirectionalFocusThresholdPx);
        Assert.Equal(2, migrated.Version);
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

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_testSettingsPath);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }
}
