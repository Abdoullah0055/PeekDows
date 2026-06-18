using System.IO;
using PeekDows.App.Startup;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class StartupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _startupFolder;
    private readonly string _exePath;
    private readonly FileLogger _logger;

    public StartupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PeekDowsTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _startupFolder = Path.Combine(_tempDir, "Startup");
        Directory.CreateDirectory(_startupFolder);
        _exePath = Path.Combine(_tempDir, "PeekDows.exe");
        File.WriteAllText(_exePath, "fake exe");
        var logDir = Path.Combine(_tempDir, "logs");
        _logger = new FileLogger(logDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private StartupService CreateService(string? startupFolder = null, string? exePath = null)
    {
        return new StartupService(
            startupFolder ?? _startupFolder,
            exePath ?? _exePath,
            Path.GetDirectoryName(_exePath)!,
            _logger);
    }

    [Fact]
    public void StartupService_IsEnabled_ReturnsFalse_WhenShortcutMissing()
    {
        var service = CreateService();
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void StartupService_Disable_IsIdempotent_WhenShortcutMissing()
    {
        var service = CreateService();
        Assert.True(service.Disable());
        Assert.True(service.Disable());
    }

    [Fact]
    public void StartupService_SetEnabledFalse_DisablesShortcut()
    {
        var service = CreateService();
        var shortcutPath = Path.Combine(_startupFolder, "PeekDows.lnk");
        File.WriteAllText(shortcutPath, "fake shortcut");

        Assert.True(service.SetEnabled(false));
        Assert.False(File.Exists(shortcutPath));
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void StartupService_Enable_ReturnsFalse_WhenExecutablePathMissing()
    {
        var service = CreateService(exePath: Path.Combine(_tempDir, "NonExistent.exe"));
        Assert.False(service.Enable());
    }

    [Fact]
    public void StartupService_Enable_ReturnsFalse_WhenExecutablePathEmpty()
    {
        var service = CreateService(exePath: "");
        Assert.False(service.Enable());
    }

    [Fact]
    public void StartupService_Enable_ReturnsFalse_WhenStartupFolderEmpty()
    {
        var service = CreateService(startupFolder: "", exePath: _exePath);
        Assert.False(service.Enable());
    }

    [Fact]
    public void StartupService_Disable_ReturnsTrue_WhenStartupFolderEmpty()
    {
        var service = CreateService(startupFolder: "", exePath: _exePath);
        Assert.True(service.Disable());
    }

    [Fact]
    public void StartupService_IsEnabled_ReturnsFalse_WhenStartupFolderEmpty()
    {
        var service = CreateService(startupFolder: "");
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void StartupService_SetEnabled_CallsEnableOrDisable()
    {
        var service = CreateService();
        Assert.True(service.SetEnabled(false));
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void StartupService_Disable_DeletesExistingFile()
    {
        var service = CreateService();
        var shortcutPath = Path.Combine(_startupFolder, "PeekDows.lnk");
        File.WriteAllText(shortcutPath, "fake shortcut");

        Assert.True(service.Disable());
        Assert.False(File.Exists(shortcutPath));
    }
}
