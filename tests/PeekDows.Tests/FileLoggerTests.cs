using System;
using System.IO;
using System.Linq;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class FileLoggerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileLogger _logger;

    public FileLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PeekDows_Test_{Guid.NewGuid():N}");
        _logger = new FileLogger(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void Info_CreatesLogFile()
    {
        _logger.Info("test message");
        Assert.True(File.Exists(_logger.LogFilePath));
    }

    [Fact]
    public void Info_WritesInfoLine()
    {
        _logger.Info("hello world");
        var content = File.ReadAllText(_logger.LogFilePath);
        Assert.Contains("[INFO]", content);
        Assert.Contains("hello world", content);
    }

    [Fact]
    public void Error_WritesErrorLine()
    {
        _logger.Error("something broke");
        var content = File.ReadAllText(_logger.LogFilePath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("something broke", content);
    }

    [Fact]
    public void Error_WithException_WritesExceptionInfo()
    {
        _logger.Error("catch", new InvalidOperationException("bad state"));
        var content = File.ReadAllText(_logger.LogFilePath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("catch", content);
        Assert.Contains("InvalidOperationException", content);
    }

    [Fact]
    public void Warn_WritesWarnLine()
    {
        _logger.Warn("be careful");
        var content = File.ReadAllText(_logger.LogFilePath);
        Assert.Contains("[WARN]", content);
        Assert.Contains("be careful", content);
    }

    [Fact]
    public void DoesNotThrowOnBasicUsage()
    {
        var exception = Record.Exception(() =>
        {
            _logger.Info("a");
            _logger.Warn("b");
            _logger.Error("c");
            _logger.Error("d", new Exception("e"));
        });
        Assert.Null(exception);
    }

    [Fact]
    public void LogFilePath_PointsToCorrectFile()
    {
        Assert.Equal(Path.Combine(_tempDir, "peekdows.log"), _logger.LogFilePath);
    }

    [Fact]
    public void MultipleWrites_AllLinesPresent()
    {
        _logger.Info("line 1");
        _logger.Info("line 2");
        _logger.Info("line 3");
        var content = File.ReadAllText(_logger.LogFilePath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
    }
}
