using System;
using System.IO;
using System.Threading;

namespace PeekDows.Core.Services;

public sealed class FileLogger
{
    private const long DefaultMaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int DefaultMaxLogBackups = 5;

    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly long _maxLogFileSizeBytes;
    private readonly int _maxLogBackups;
    private readonly object _lock = new();

    public string LogFilePath => _logFilePath;

    public FileLogger() : this(logDirectory: null) { }

    public FileLogger(string? logDirectory) : this(logDirectory, maxLogFileSizeBytes: null, maxLogBackups: null) { }

    /// <summary>
    /// Testable constructor. Production code uses the parameterless / directory-only overloads
    /// and inherits the default rotation limits. Tests inject small limits to trigger rotation
    /// without writing megabytes.
    /// </summary>
    internal FileLogger(string? logDirectory, long? maxLogFileSizeBytes, int? maxLogBackups)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logDirectory = Path.Combine(appData, "PeekDows", "logs");
        }
        else
        {
            _logDirectory = logDirectory;
        }

        _logFilePath = Path.Combine(_logDirectory, "peekdows.log");
        _maxLogFileSizeBytes = maxLogFileSizeBytes ?? DefaultMaxLogFileSizeBytes;
        _maxLogBackups = maxLogBackups ?? DefaultMaxLogBackups;
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception ex)
    {
        Write("ERROR", $"{message}: {ex}");
    }

    private void Write(string level, string message)
    {
        try
        {
            Monitor.Enter(_lock);
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                RotateIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
        catch
        {
            // Logging must never crash the app. If writing or rotation fails, swallow it.
        }
    }

    /// <summary>
    /// Rotates the active log file when it meets or exceeds the configured size limit. Rotation
    /// shifts backups forward (peekdows.4.log -> peekdows.5.log, ..., peekdows.log -> peekdows.1.log)
    /// and drops the oldest backup once MaxLogBackups is reached. Missing backup files are skipped
    /// silently so rotation stays safe on a fresh or partially-rotated directory.
    /// </summary>
    private void RotateIfNeeded()
    {
        try
        {
            if (_maxLogFileSizeBytes <= 0 || _maxLogBackups <= 0) return;
            if (!File.Exists(_logFilePath)) return;
            var length = new FileInfo(_logFilePath).Length;
            if (length < _maxLogFileSizeBytes) return;

            // C9 fix: retry with overwrite semantics and surface failures via Debug.
            var oldestPath = BackupPath(_maxLogBackups);
            if (File.Exists(oldestPath))
            {
                try { File.Delete(oldestPath); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FileLogger rotate delete oldest failed: {ex.Message}"); }
            }
            for (var n = _maxLogBackups; n >= 2; n--)
            {
                var from = BackupPath(n - 1);
                var to = BackupPath(n);
                if (File.Exists(from))
                {
                    try
                    {
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(from, to);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FileLogger rotate move {from}->{to} failed: {ex.Message}"); }
                }
            }
            var firstBackup = BackupPath(1);
            try
            {
                if (File.Exists(firstBackup)) File.Delete(firstBackup);
                File.Move(_logFilePath, firstBackup);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FileLogger rotate promote failed: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FileLogger RotateIfNeeded failed: {ex.Message}");
        }
    }

    private string BackupPath(int index) => Path.Combine(_logDirectory, $"peekdows.{index}.log");
}
