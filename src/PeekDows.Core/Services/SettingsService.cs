using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _settingsDirectory;

    private static readonly List<string> DefaultIgnoredProcesses = new()
    {
        "SearchHost.exe",
        "StartMenuExperienceHost.exe",
        "ShellExperienceHost.exe",
        "TextInputHost.exe",
        "LockApp.exe",
        "PeekDows.exe",
        "SystemSettings.exe"
    };

    private static readonly List<string> DefaultIgnoredClasses = new()
    {
        "Shell_TrayWnd",
        "WorkerW",
        "Progman",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "DV2ControlHost",
        "Windows.UI.Composition.DesktopWindowContentBridge"
    };

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsDirectory = Path.Combine(appData, "PeekDows");
        _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
    }

    public SettingsService(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
        _settingsDirectory = Path.GetDirectoryName(settingsFilePath) ?? string.Empty;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return MigrateIfNeeded(settings ?? CreateDefault());
        }
        catch
        {
            if (File.Exists(_settingsFilePath))
            {
                var backupPath = Path.Combine(_settingsDirectory, $"settings.corrupted.{DateTime.Now:yyyyMMddHHmmss}.json");
                File.Copy(_settingsFilePath, backupPath, overwrite: true);
            }
            return CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        if (!Directory.Exists(_settingsDirectory))
        {
            Directory.CreateDirectory(_settingsDirectory);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(settings, options);
        File.WriteAllText(_settingsFilePath, json);
    }

    public AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        Save(settings);
        return settings;
    }

    public AppSettings MigrateIfNeeded(AppSettings settings)
    {
        settings.IgnoredProcesses ??= new List<string>();
        settings.IgnoredClasses ??= new List<string>();
        MergeMissingDefaults(settings.IgnoredProcesses, DefaultIgnoredProcesses);
        MergeMissingDefaults(settings.IgnoredClasses, DefaultIgnoredClasses);
        return settings;
    }

    private static void MergeMissingDefaults(List<string> current, List<string> defaults)
    {
        if (current == null)
        {
            return;
        }

        var existing = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var item in defaults)
        {
            if (!existing.Contains(item))
            {
                current.Add(item);
            }
        }
    }
}
