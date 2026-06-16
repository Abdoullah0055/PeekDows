using System;
using System.IO;
using System.Threading;

namespace PeekDows.Core.Services;

public sealed class FileLogger
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public string LogFilePath => _logFilePath;

    public FileLogger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDirectory = Path.Combine(appData, "PeekDows", "logs");
        _logFilePath = Path.Combine(_logDirectory, "peekdows.log");
    }

    public FileLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        _logFilePath = Path.Combine(_logDirectory, "peekdows.log");
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
        }
    }
}
