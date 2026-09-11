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
            if (settings == null)
            {
                settings = CreateDefault();
                return settings;
            }
            int originalVersion = settings.Version;
            var migrated = MigrateIfNeeded(settings);
            if (migrated.Version != originalVersion)
            {
                try { Save(migrated); } catch { /* best-effort persist, not fatal */ }
            }
            return migrated;
        }
        catch
        {
            if (File.Exists(_settingsFilePath))
            {
                var backupPath = Path.Combine(_settingsDirectory, $"settings.corrupted.{DateTime.UtcNow:yyyyMMddHHmmss}.json");
                try { File.Copy(_settingsFilePath, backupPath, overwrite: true); } catch { }
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
        // Atomic write: tmp file + Move overwrite (atomic on NTFS). Prevents a crash or
        // power loss mid-write from leaving a truncated settings.json that would be
        // treated as "corrupted" on next Load() and reset user prefs to defaults.
        var tmpPath = _settingsFilePath + ".tmp";
        var maxRetries = 2;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.WriteAllText(tmpPath, json);
                // .NET 6+ overload: overwrite:true makes this atomic on Windows.
                File.Move(tmpPath, _settingsFilePath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt + 1 < maxRetries)
            {
                // P-C6: avoid blocking UI thread — just SpinWait then retry iteration.
                // No Task.Delay().Wait() (would block tray). Transient AV/indexer lock
                // usually clears in microseconds; retry covers it.
                System.Threading.Thread.SpinWait(5000);
            }
            catch
            {
                // Cleanup tmp on any non-IO failure before bubbling / retrying.
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                if (attempt + 1 >= maxRetries) throw;
            }
        }
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

        // --- Migration to schema v3: SingleWindowMode retired (lone-window maximize is now
        // per-monitor behavior, not a setting) and window transition animation added
        // (default true). No value transformation needed: the JSON deserializer ignores the
        // retired property, and a missing AnimateWindowTransitions falls back to true. ---
        if (settings.Version < 3)
        {
            settings.Version = 3;
        }

#pragma warning disable CS0618 // OverflowBehavior is deprecated
        if (!string.Equals(settings.OverflowBehavior, "Ignore", StringComparison.OrdinalIgnoreCase))
        {
            // User edited settings.json expecting it to do something — log once.
            // Don't throw; just normalize so future saves don't preserve surprising values.
            System.Diagnostics.Debug.WriteLine($"Settings: OverflowBehavior='{settings.OverflowBehavior}' is deprecated and ignored.");
        }
#pragma warning restore CS0618

        // Clamp invalid values to the current default, not the legacy 80.
        if (settings.DirectionalFocusThresholdPx <= 0)
            settings.DirectionalFocusThresholdPx = 50;

        return settings;
    }
}
