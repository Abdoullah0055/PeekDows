using System;
using System.IO;
using System.Text.Json;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _settingsDirectory;

    /// <summary>Full path of the settings.json file managed by this instance.</summary>
    public string SettingsFilePath => _settingsFilePath;

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

    /// <summary>
    /// Legacy (v4) canonicalization, kept for the v3→v4 migration step: valid
    /// Off/Overlay/Spotlight/Both keep canonical casing, unknown → "Both".
    /// The subsequent v5 step remaps to Off|Overlay.
    /// </summary>
    private static string NormalizeFocusHintMode(string? value)
    {
        if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase)) return "Off";
        if (string.Equals(value, "Overlay", StringComparison.OrdinalIgnoreCase)) return "Overlay";
        if (string.Equals(value, "Spotlight", StringComparison.OrdinalIgnoreCase)) return "Spotlight";
        if (string.Equals(value, "Both", StringComparison.OrdinalIgnoreCase)) return "Both";
        return "Both";
    }

    /// <summary>
    /// Canonicalizes a FocusHintMode value (case-insensitive). Unknown, empty or null
    /// values fall back to "Overlay". Valid Off/Overlay choices keep their canonical
    /// casing; legacy Both/Spotlight values map to Overlay/Off respectively.
    /// </summary>
    private static string NormalizeFocusHintModeV5(string? value)
    {
        if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase)) return "Off";
        if (string.Equals(value, "Overlay", StringComparison.OrdinalIgnoreCase)) return "Overlay";
        if (string.Equals(value, "Both", StringComparison.OrdinalIgnoreCase)) return "Overlay";
        if (string.Equals(value, "Spotlight", StringComparison.OrdinalIgnoreCase)) return "Off";
        return "Overlay";
    }

    public AppSettings MigrateIfNeeded(AppSettings settings)
    {        settings.IgnoredProcesses ??= new List<string>();
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

        // --- Migration to schema v4: focus hint settings added (FocusHintMode default
        // "Both", OverlayShowAppIcons default true, SpotlightDimOpacity default 45).
        // Existing installs (Version < 4) have no persisted values, so the property
        // initializers already supply the defaults. Only normalize invalid data:
        // unknown/empty FocusHintMode → "Both" (case-insensitive canonicalization preserves
        // any valid existing choice). The spotlight clamp was dropped with schema v5
        // (SpotlightDimOpacity retired); a stale spotlightOpacity key in old JSON is
        // ignored by the deserializer (unknown properties are skipped by default). ---
        if (settings.Version < 4)
        {
            settings.FocusHintMode = NormalizeFocusHintMode(settings.FocusHintMode);
            settings.Version = 4;
        }

        // --- Migration to schema v5: spotlight retired, OverlayShowAppIcons retired.
        // FocusHintMode is now Off | Overlay (default "Overlay"). Mapping (case-insensitive):
        // Both → Overlay, Spotlight → Off, Off → Off, Overlay → Overlay;
        // null/empty/unknown → "Overlay". Valid Off/Overlay values are never overwritten,
        // only re-cased to canonical form. ---
        if (settings.Version < 5)
        {
            settings.FocusHintMode = NormalizeFocusHintModeV5(settings.FocusHintMode);
            settings.Version = 5;
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
