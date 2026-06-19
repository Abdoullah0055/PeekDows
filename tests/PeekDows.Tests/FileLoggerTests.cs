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

    // --- Rotation tests. The internal constructor lets tests inject a tiny size limit so rotation
    //     can be exercised without writing megabytes. Backup format is peekdows.{n}.log. ---

    private static string BackupPath(string dir, int n) => Path.Combine(dir, $"peekdows.{n}.log");

    [Fact]
    public void FileLogger_WritesToLogFile()
    {
        _logger.Info("rotation baseline");

        Assert.True(File.Exists(_logger.LogFilePath));
        Assert.Contains("rotation baseline", File.ReadAllText(_logger.LogFilePath));
    }

    [Fact]
    public void FileLogger_DoesNotRotate_WhenBelowMaxSize()
    {
        var logger = new FileLogger(_tempDir, maxLogFileSizeBytes: 1024, maxLogBackups: 3);
        logger.Info("small entry");

        Assert.True(File.Exists(logger.LogFilePath));
        Assert.False(File.Exists(BackupPath(_tempDir, 1)));
        Assert.Contains("small entry", File.ReadAllText(logger.LogFilePath));
    }

    [Fact]
    public void FileLogger_Rotates_WhenFileExceedsMaxSize()
    {
        // Seed an active file already at/over the limit, then a single new write must trigger
        // rotation: peekdows.log -> peekdows.1.log, and a fresh peekdows.log is created.
        var logger = new FileLogger(_tempDir, maxLogFileSizeBytes: 64, maxLogBackups: 3);
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(logger.LogFilePath, new string('x', 100)); // over the 64-byte limit

        logger.Info("trigger rotation");

        Assert.True(File.Exists(BackupPath(_tempDir, 1)));
        Assert.True(File.Exists(logger.LogFilePath));
        // The rotated backup keeps the oversized content; the new active file has the fresh line.
        Assert.Contains("trigger rotation", File.ReadAllText(logger.LogFilePath));
        Assert.Equal(100, new FileInfo(BackupPath(_tempDir, 1)).Length);
    }

    [Fact]
    public void FileLogger_KeepsOnlyMaxBackups()
    {
        const int maxBackups = 3;
        var logger = new FileLogger(_tempDir, maxLogFileSizeBytes: 32, maxLogBackups: maxBackups);
        Directory.CreateDirectory(_tempDir);

        // Force many rotations: each write exceeds the 32-byte limit, so each write rotates.
        for (int i = 0; i < 10; i++)
        {
            logger.Info($"rotation number {i} with enough padding to exceed the limit");
        }

        Assert.True(File.Exists(logger.LogFilePath));
        Assert.True(File.Exists(BackupPath(_tempDir, 1)));
        Assert.True(File.Exists(BackupPath(_tempDir, 2)));
        Assert.True(File.Exists(BackupPath(_tempDir, 3)));
        // Anything beyond MaxLogBackups must have been dropped.
        Assert.False(File.Exists(BackupPath(_tempDir, 4)));
        Assert.False(File.Exists(BackupPath(_tempDir, 5)));
    }

    [Fact]
    public void FileLogger_RotatesExistingBackupsInOrder()
    {
        var logger = new FileLogger(_tempDir, maxLogFileSizeBytes: 32, maxLogBackups: 3);
        Directory.CreateDirectory(_tempDir);

        // Pre-create a chain: active + 2 backups. Mark each so we can verify the shift direction.
        File.WriteAllText(logger.LogFilePath, new string('a', 50));      // active (over limit)
        File.WriteAllText(BackupPath(_tempDir, 1), "B1");
        File.WriteAllText(BackupPath(_tempDir, 2), "B2");

        logger.Info("rotate now"); // triggers: .2->.3, .1->.2, active->.1, new active created

        // The old active (all 'a's) is now the newest backup peekdows.1.log.
        Assert.Equal(new string('a', 50), File.ReadAllText(BackupPath(_tempDir, 1)));
        // The previous .1 and .2 shifted forward by exactly one.
        Assert.Equal("B1", File.ReadAllText(BackupPath(_tempDir, 2)));
        Assert.Equal("B2", File.ReadAllText(BackupPath(_tempDir, 3)));
        // A fresh active file exists and contains the line that triggered rotation.
        Assert.Contains("rotate now", File.ReadAllText(logger.LogFilePath));
    }

    [Fact]
    public void FileLogger_DoesNotThrow_WhenBackupFilesMissing()
    {
        // Fresh directory, no backups at all, active file over the limit. Rotation must still work
        // (promote active to .1) and never throw despite the missing chain.
        var logger = new FileLogger(_tempDir, maxLogFileSizeBytes: 16, maxLogBackups: 3);
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(logger.LogFilePath, new string('z', 40));

        var exception = Record.Exception(() => logger.Info("first rotation"));

        Assert.Null(exception);
        Assert.True(File.Exists(BackupPath(_tempDir, 1)));
        Assert.True(File.Exists(logger.LogFilePath));
        Assert.False(File.Exists(BackupPath(_tempDir, 2)));
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
