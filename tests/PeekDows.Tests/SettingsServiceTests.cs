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
        // Act
        var settings = _settingsService.Load();

        // Assert
        Assert.NotNull(settings);
        Assert.True(File.Exists(_testSettingsPath));
        Assert.False(settings.AutoArrange);
    }

    [Fact]
    public void Save_And_Load_PersistsValues()
    {
        // Arrange
        var settings = _settingsService.Load();
        settings.AutoArrange = true;
        _settingsService.Save(settings);

        // Act
        var loadedSettings = _settingsService.Load();

        // Assert
        Assert.True(loadedSettings.AutoArrange);
    }

    [Fact]
    public void Load_WhenFileCorrupt_CreatesBackupAndReturnsDefault()
    {
        // Arrange
        File.WriteAllText(_testSettingsPath, "{ CorruptedJSON");

        // Act
        var settings = _settingsService.Load();

        // Assert
        Assert.NotNull(settings);
        Assert.False(settings.AutoArrange); // Should be default
        
        var directory = Path.GetDirectoryName(_testSettingsPath);
        var files = Directory.GetFiles(directory, "settings.corrupted.*.json");
        Assert.Single(files);
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
