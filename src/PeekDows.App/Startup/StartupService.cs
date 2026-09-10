using System;
using System.IO;
using PeekDows.Core.Services;

namespace PeekDows.App.Startup;

public sealed class StartupService : IStartupService
{
    private const string ShortcutName = "PeekDows.lnk";

    private readonly string _startupFolderPath;
    private readonly string _executablePath;
    private readonly string _workingDirectory;
    private readonly FileLogger? _logger;

    public StartupService() : this(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.ProcessPath ?? string.Empty,
        AppContext.BaseDirectory,
        null)
    {
    }

    public StartupService(FileLogger logger) : this(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.ProcessPath ?? string.Empty,
        AppContext.BaseDirectory,
        logger)
    {
    }

    internal StartupService(string startupFolderPath, string executablePath, string workingDirectory, FileLogger? logger)
    {
        _startupFolderPath = startupFolderPath;
        _executablePath = executablePath;
        _workingDirectory = workingDirectory;
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            if (string.IsNullOrEmpty(_startupFolderPath))
                return false;

            var shortcutPath = GetShortcutPath();
            return File.Exists(shortcutPath);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"StartupService: failed to check startup shortcut: {ex.Message}");
            return false;
        }
    }

    public bool Enable()
    {
        try
        {
            if (string.IsNullOrEmpty(_startupFolderPath))
            {
                _logger?.Warn("StartupService: startup folder path is empty");
                return false;
            }

            if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_executablePath))
            {
                _logger?.Warn("StartupService: executable path is missing or invalid");
                return false;
            }

            var shortcutPath = GetShortcutPath();

            if (File.Exists(shortcutPath))
            {
                _logger?.Info("StartupService: startup shortcut already enabled");
                return true;
            }

            if (!CreateShortcut(shortcutPath))
            {
                _logger?.Warn($"StartupService: failed to enable startup shortcut");
                return false;
            }

            _logger?.Info("StartupService: startup shortcut enabled");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"StartupService: failed to enable startup shortcut: {ex.Message}");
            return false;
        }
    }

    public bool Disable()
    {
        try
        {
            if (string.IsNullOrEmpty(_startupFolderPath))
            {
                _logger?.Warn("StartupService: startup folder path is empty");
                return true;
            }

            var shortcutPath = GetShortcutPath();

            if (!File.Exists(shortcutPath))
            {
                _logger?.Info("StartupService: startup shortcut already disabled");
                return true;
            }

            File.Delete(shortcutPath);
            _logger?.Info("StartupService: startup shortcut disabled");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"StartupService: failed to disable startup shortcut: {ex.Message}");
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        return enabled ? Enable() : Disable();
    }

    private string GetShortcutPath()
    {
        return Path.Combine(_startupFolderPath, ShortcutName);
    }

    private bool CreateShortcut(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                _logger?.Warn("StartupService: WScript.Shell COM object not available");
                return false;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                _logger?.Warn("StartupService: failed to create WScript.Shell instance");
                return false;
            }

            object? shortcut = null;
            try
            {
                shortcut = shell.CreateShortcut(shortcutPath);
                dynamic dyn = shortcut;
                dyn.TargetPath = _executablePath;
                dyn.WorkingDirectory = _workingDirectory;
                dyn.Description = "PeekDows";
                dyn.IconLocation = $"{_executablePath},0";
                dyn.Save();
                return true;
            }
            finally
            {
                if (shortcut != null)
                    try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); } catch { }
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"StartupService: failed to create shortcut: {ex.Message}");
            return false;
        }
    }
}
