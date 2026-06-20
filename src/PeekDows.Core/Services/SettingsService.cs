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
        settings.Hotkeys ??= new Dictionary<string, string>();

        // --- Migration to schema v2: lower the gesture threshold default. ---
        // Older installs persisted DirectionalFocusThresholdPx = 80 (the original default),
        // which made the Ctrl+Shift shortcut feel heavy. Users who never customised it get
        // migrated to the new, lighter default (50). Users who set a non-80 value are left
        // alone (their explicit choice is preserved).
        if (settings.Version < 2)
        {
            if (settings.DirectionalFocusThresholdPx == 80)
            {
                settings.DirectionalFocusThresholdPx = 50;
            }
            settings.Version = 2;
        }

        // Clamp invalid values to the current default, not the legacy 80.
        if (settings.DirectionalFocusThresholdPx <= 0)
            settings.DirectionalFocusThresholdPx = 50;

        return settings;
    }
}
