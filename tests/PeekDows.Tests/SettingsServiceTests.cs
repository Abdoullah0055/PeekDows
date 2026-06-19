using System;
using System.IO;
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

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_testSettingsPath);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }
}
