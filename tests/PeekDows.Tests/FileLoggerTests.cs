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

    private void FlushLogger(FileLogger logger)
    {
        logger.Flush();
        // Also drain the shared _logger queue if different instance
        if (!ReferenceEquals(logger, _logger)) _logger.Flush();
    }

    [Fact]
    public void Info_CreatesLogFile()
    {
        _logger.Info("test message");
        _logger.Flush();
        Assert.True(File.Exists(_logger.LogFilePath));
    }

    [Fact]
    public void Info_WritesInfoLine()
    {
        _logger.Info("hello world");
        _logger.Flush();
        var content = File.ReadAllText(_logger.LogFilePath);
        Assert.Contains("[INFO]", content);
        Assert.Contains("hello world", content);
    }

    [Fact]
    public void Error_WritesErrorLine()
    {
        _logger.Error("something broke");
        _logger.Flush();
        var content = File.ReadAllText(_logger.LogFilePath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("something broke", content);
    }

    [Fact]
    public void Error_WithException_WritesExceptionInfo()
    {
        _logger.Error("catch", new InvalidOperationException("bad state"));
        _logger.Flush();
        var content = File.ReadAllText(_logger.LogFilePath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("catch", content);
        Assert.Contains("InvalidOperationException", content);
    }

    [Fact]
    public void Warn_WritesWarnLine()
    {
        _logger.Warn("be careful");
        _logger.Flush();
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
        var isoDir = Path.Combine(Path.GetTempPath(), $"PeekDows_TestIso_{Guid.NewGuid():N}");
        var isoLogger = new FileLogger(isoDir);
        try
        {
            isoLogger.Info("line 1");
            isoLogger.Info("line 2");
            isoLogger.Info("line 3");
            isoLogger.Flush();
            // Retry: async writer can still be flushing for ~15ms after Flush drain.
            string content = "";
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try { content = File.ReadAllText(isoLogger.LogFilePath); } catch { Thread.Sleep(20); continue; }
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 3) break;
                Thread.Sleep(20);
            }
            var finalLines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, finalLines.Length);
        }
        finally { try { Directory.Delete(isoDir, true); } catch { } }
    }

    // --- Rotation tests. The internal constructor lets tests inject a tiny size limit so rotation
    //     can be exercised without writing megabytes. Backup format is peekdows.{n}.log. ---

    private static string BackupPath(string dir, int n) => Path.Combine(dir, $"peekdows.{n}.log");

    [Fact]
    public void FileLogger_WritesToLogFile()
    {
        _logger.Info("rotation baseline");
        _logger.Flush();
        Assert.True(File.Exists(_logger.LogFilePath));
        Assert.Contains("rotation baseline", File.ReadAllText(_logger.LogFilePath));
    }

    [Fact]
    public void FileLogger_DoesNotRotate_WhenBelowMaxSize()
    {
        var isoDir = Path.Combine(Path.GetTempPath(), $"PeekDows_TestIso_{Guid.NewGuid():N}");
        var logger = new FileLogger(isoDir, maxLogFileSizeBytes: 1024, maxLogBackups: 3);
        try
        {
            logger.Info("small entry");
            logger.Flush();
            Assert.True(File.Exists(logger.LogFilePath));
            Assert.False(File.Exists(BackupPath(isoDir, 1)));
            Assert.Contains("small entry", File.ReadAllText(logger.LogFilePath));
        }
        finally { try { Directory.Delete(isoDir, true); } catch { } }
    }

    [Fact]
    public void FileLogger_Rotates_WhenFileExceedsMaxSize()
    {
        var isoDir = Path.Combine(Path.GetTempPath(), $"PeekDows_TestIso_{Guid.NewGuid():N}");
        var logger = new FileLogger(isoDir, maxLogFileSizeBytes: 64, maxLogBackups: 3);
        try
        {
            Directory.CreateDirectory(isoDir);
            File.WriteAllText(logger.LogFilePath, new string('x', 100));
            logger.Info("trigger rotation");
            logger.Flush();
            Assert.True(File.Exists(BackupPath(isoDir, 1)));
            Assert.True(File.Exists(logger.LogFilePath));
            Assert.Contains("trigger rotation", File.ReadAllText(logger.LogFilePath));
            Assert.Equal(100, new FileInfo(BackupPath(isoDir, 1)).Length);
        }
        finally { try { Directory.Delete(isoDir, true); } catch { } }
    }

    [Fact]
    public void FileLogger_KeepsOnlyMaxBackups()
    {
        // Isolated dir: two loggers sharing _tempDir race on rotation (previous failure).
        var isoDir = Path.Combine(Path.GetTempPath(), $"PeekDows_TestIso_{Guid.NewGuid():N}");
        var logger = new FileLogger(isoDir, maxLogFileSizeBytes: 32, maxLogBackups: 3);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                logger.Info($"rotation number {i} with enough padding to exceed the limit");
                logger.Flush();
            }
            Assert.True(File.Exists(logger.LogFilePath));
            Assert.True(File.Exists(BackupPath(isoDir, 1)));
            Assert.True(File.Exists(BackupPath(isoDir, 2)));
            Assert.True(File.Exists(BackupPath(isoDir, 3)));
            Assert.False(File.Exists(BackupPath(isoDir, 4)));
            Assert.False(File.Exists(BackupPath(isoDir, 5)));
        }
        finally { try { Directory.Delete(isoDir, true); } catch { } }
    }

    [Fact]
    public void FileLogger_RotatesExistingBackupsInOrder()
    {
        var isoDir = Path.Combine(Path.GetTempPath(), $"PeekDows_TestIso_{Guid.NewGuid():N}");
        var logger = new FileLogger(isoDir, maxLogFileSizeBytes: 32, maxLogBackups: 3);
        try
        {
            Directory.CreateDirectory(isoDir);
            File.WriteAllText(logger.LogFilePath, new string('a', 50));
            File.WriteAllText(BackupPath(isoDir, 1), "B1");
            File.WriteAllText(BackupPath(isoDir, 2), "B2");
            logger.Info("rotate now");
            logger.Flush();
            Assert.Equal(new string('a', 50), File.ReadAllText(BackupPath(isoDir, 1)));
            Assert.Equal("B1", File.ReadAllText(BackupPath(isoDir, 2)));
            Assert.Equal("B2", File.ReadAllText(BackupPath(isoDir, 3)));
            Assert.Contains("rotate now", File.ReadAllText(logger.LogFilePath));
        }
        finally { try { Directory.Delete(isoDir, true); } catch { } }
    }

    [Fact]
    public void FileLogger_DoesNotThrow_WhenBackupFilesMissing()
    {
        var isoDir = Path.Combine(Path.GetTempPath(), $"PeekDows_TestIso_{Guid.NewGuid():N}");
        var logger = new FileLogger(isoDir, maxLogFileSizeBytes: 16, maxLogBackups: 3);
        try
        {
            Directory.CreateDirectory(isoDir);
            File.WriteAllText(logger.LogFilePath, new string('z', 40));
            var exception = Record.Exception(() => { logger.Info("first rotation"); logger.Flush(); });
            Assert.Null(exception);
            Assert.True(File.Exists(BackupPath(isoDir, 1)));
            Assert.True(File.Exists(logger.LogFilePath));
            Assert.False(File.Exists(BackupPath(isoDir, 2)));
        }
        finally { try { Directory.Delete(isoDir, true); } catch { } }
    }

    [Fact]
    public void FileLogger_Rotation_NeverThrows_OnTransientIoFailure()
    {
        // If the log directory cannot be written to (e.g. path is a file, not a directory), the
        // logger must swallow the error and never propagate to callers. This mirrors the existing
        // best-effort logging contract.
        var badDir = Path.Combine(_tempDir, "not_a_dir");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(badDir, "blocker"); // badDir is a file, so creating logs under it fails

        var logger = new FileLogger(badDir, maxLogFileSizeBytes: 16, maxLogBackups: 3);

        var exception = Record.Exception(() => logger.Info("should not throw"));

        Assert.Null(exception);
    }
}
