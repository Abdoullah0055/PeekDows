using System;
using System.IO;
using System.Text.Json;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _settingsDirectory;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsDirectory = Path.Combine(appData, "PeekDows");
        _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
    }

    // For testing purposes
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
            // Corrupted config -> Backup and recreate default
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
        // Add migration logic if Version increases in the future
        return settings;
    }
}
