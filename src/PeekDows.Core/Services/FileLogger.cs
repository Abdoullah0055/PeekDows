using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace PeekDows.Core.Services;

public sealed class FileLogger : IDisposable
{
    private const long DefaultMaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int DefaultMaxLogBackups = 5;
    // P-A2 fix: async batch queue — Write() enqueues, background thread does IO.
    // Batching still gives the major win (1 AppendAllText per 64 lines vs per line).
    // Rotation check stays per batch (not per 16) so tiny-limit tests remain correct.
    private const int BatchSize = 64;


    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly long _maxLogFileSizeBytes;
    private readonly int _maxLogBackups;
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly Thread _writerThread;
    private readonly object _fileLock = new();
    private int _pendingWrites;
    private bool _disposed;

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
        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "PeekDows FileLogger" };
        _writerThread.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
        try { _queue.CompleteAdding(); } catch { }
        try { if (!_writerThread.Join(1500)) { } } catch { }
        _queue.Dispose();
    }

    public void Flush()
    {
        for (int i = 0; i < 120; i++)
        {
            if (_queue.Count == 0 && Volatile.Read(ref _pendingWrites) == 0)
            {
                lock (_fileLock) { }
                if (_queue.Count == 0 && Volatile.Read(ref _pendingWrites) == 0) break;
            }
            Thread.Sleep(10);
        }
        lock (_fileLock) { }
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
            var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{level}] {message}{Environment.NewLine}";
            if (!_queue.IsAddingCompleted)
            {
                Interlocked.Increment(ref _pendingWrites);
                bool added = _queue.TryAdd(line);
                if (!added) Interlocked.Decrement(ref _pendingWrites);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    private void WriterLoop()
    {
        var batch = new List<string>(BatchSize);
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                batch.Add(line);
                while (batch.Count < BatchSize && _queue.TryTake(out var extra))
                    batch.Add(extra);
                WriteBatch(batch);
                Interlocked.Add(ref _pendingWrites, -batch.Count);
                batch.Clear();
            }
            while (_queue.TryTake(out var rem)) batch.Add(rem);
            if (batch.Count > 0) { WriteBatch(batch); Interlocked.Add(ref _pendingWrites, -batch.Count); }
        }
        catch { }
    }

    private void WriteBatch(List<string> batch)
    {
        lock (_fileLock)
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);
                RotateIfNeeded();
                File.AppendAllText(_logFilePath, string.Concat(batch));
            }
            catch { }
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
