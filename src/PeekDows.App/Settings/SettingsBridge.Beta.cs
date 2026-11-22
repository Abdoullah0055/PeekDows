using System;
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Sound;

namespace PeekDows.App.Settings;

/// <summary>
/// Beta settings bridge (Agent C): focus hint mode (Off|Overlay) + arrange sound
/// (soundEnabled/soundName/soundAutoOffMinutes/userSounds + pick/preview/delete)
/// + layout mode (layoutMode) + per-app rules (appRules).
/// Backed by Agent 1's AppSettings (FocusHintMode, ArrangeSound*).
/// </summary>
public sealed partial class SettingsBridge
{
    private bool _soundPickerPending;

    private void AugmentSnapshotBeta(Dictionary<string, object?> data)
    {
        var s = _controller.CurrentSettings;
        data["focusHintMode"] = s.FocusHintMode ?? "Overlay";
        data["layoutMode"] = s.LayoutMode ?? "Grid";
        data["customZones"] = s.CustomZones ?? new Core.Models.CustomZoneLayout();
        data["appRules"] = s.AppRules ?? new List<Core.Models.AppRule>();
        data["overlayShowIcons"] = s.OverlayShowAppIcons;
        data["soundEnabled"] = s.ArrangeSoundEnabled;
        data["soundName"] = PersistentSoundName(s);
        data["soundAutoOffMinutes"] = s.ArrangeSoundAutoOffMinutes;
        data["userSounds"] = ReadUserSounds();
    }

    private static string PersistentSoundName(Core.Models.AppSettings settings)
        => string.IsNullOrEmpty(settings.ArrangeSoundName) ? "chime-soft" : settings.ArrangeSoundName;

    private bool IsUnavailableSoundChange(string? requested, string? current)
        => !string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested, current, StringComparison.Ordinal)
            && !ArrangeSoundService.IsFactorySound(requested)
            && !ReadUserSounds().Contains(requested, StringComparer.OrdinalIgnoreCase);

    private string[] ReadUserSounds()
    {
        try
        {
            return _controller.GetUserSounds() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void ApplyBetaDraft(SettingsDraft draft, Core.Models.AppSettings settings, ref bool changed)
    {
        if (draft.FocusHintMode is { } raw
            && TryNormalizeHintMode(raw, out var mode)
            && !string.Equals(settings.FocusHintMode, mode, StringComparison.Ordinal))
        {
            settings.FocusHintMode = mode;
            changed = true;
        }

        if (draft.SoundEnabled is { } se && settings.ArrangeSoundEnabled != se)
        {
            settings.ArrangeSoundEnabled = se;
            changed = true;
        }

        if (draft.OverlayShowIcons is { } oi && settings.OverlayShowAppIcons != oi)
        {
            settings.OverlayShowAppIcons = oi;
            changed = true;
        }

        if (draft.SoundName is { } sn
            && !string.IsNullOrWhiteSpace(sn)
            && !string.Equals(settings.ArrangeSoundName, sn, StringComparison.Ordinal))
        {
            settings.ArrangeSoundName = sn;
            changed = true;
        }

        if (draft.SoundAutoOffMinutes is { } minutes
            && minutes >= 0
            && settings.ArrangeSoundAutoOffMinutes != minutes)
        {
            settings.ArrangeSoundAutoOffMinutes = minutes;
            changed = true;
        }

        if (draft.LayoutMode is { } rawLayout
            && TryNormalizeLayoutMode(rawLayout, out var layoutMode)
            && !string.Equals(settings.LayoutMode, layoutMode, StringComparison.Ordinal))
        {
            settings.LayoutMode = layoutMode;
            changed = true;
        }

        if (draft.AppRules is { } rules)
        {
            var clean = Core.Services.AppRuleService.CleanRules(rules);
            if (!Core.Services.AppRuleService.AreRulesEqual(settings.AppRules, clean))
            {
                settings.AppRules = clean;
                changed = true;
            }
        }
    }

    /// <summary>
    /// Handles pickSoundFile / previewSound / deleteSoundFile. Called from the
    /// HandleMessage switch (SettingsBridge.cs). Never throws.
    /// </summary>
    private string? TryHandleSoundMessage(string type, JsonElement root)
    {
        if (type == "pickSoundFile")
            return QueueSoundPicker();

        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? ""
            : "";
        if (type == "previewSound")
        {
            try { _controller.PreviewArrangeSound(name); } catch { }
            return null;
        }

        try
        {
            _controller.DeleteArrangeSoundFile(name);
            return JsonSerializer.Serialize(new
            {
                type = "soundFile",
                deletedName = name,
                soundName = PersistentSoundName(_controller.CurrentSettings),
                userSounds = ReadUserSounds(),
            }, WriteOptions);
        }
        catch (Exception ex)
        {
            return SoundError("delete", ex);
        }
    }

    private string? QueueSoundPicker()
    {
        if (_soundPickerPending) return null;
        _soundPickerPending = true;
        try
        {
            _runDialog(PickSoundFile);
            return null;
        }
        catch (Exception ex)
        {
            _soundPickerPending = false;
            return SoundError("pick", ex);
        }
    }

    private void PickSoundFile()
    {
        try
        {
            string? response = null;
            try
            {
                var name = _controller.PickArrangeSoundFile();
                if (!string.IsNullOrEmpty(name))
                    response = JsonSerializer.Serialize(new { type = "soundFile", name, userSounds = ReadUserSounds() }, WriteOptions);
            }
            catch (Exception ex)
            {
                response = SoundError("pick", ex);
            }

            if (response is not null)
                _postAsyncResponse?.Invoke(response);
        }
        finally
        {
            _soundPickerPending = false;
        }
    }

    private string SoundError(string operation, Exception error)
    {
        _logger?.Warn($"SettingsBridge: {operation} sound failed: {error.Message}");
        return JsonSerializer.Serialize(new { type = "soundError", operation }, WriteOptions);
    }

    private static bool TryNormalizeLayoutMode(string raw, out string mode)
    {
        if (string.Equals(raw, "Custom", StringComparison.OrdinalIgnoreCase)) { mode = "Custom"; return true; }
        if (string.Equals(raw, "Grid", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "g", StringComparison.OrdinalIgnoreCase)) { mode = "Grid"; return true; }
        if (string.Equals(raw, "Halves", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "h", StringComparison.OrdinalIgnoreCase)) { mode = "Halves"; return true; }
        if (string.Equals(raw, "Thirds", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "t", StringComparison.OrdinalIgnoreCase)) { mode = "Thirds"; return true; }
        mode = string.Empty;
        return false;
    }

    private static bool TryNormalizeHintMode(string raw, out string mode)
    {
        if (string.Equals(raw, "Off", StringComparison.OrdinalIgnoreCase)) { mode = "Off"; return true; }
        if (string.Equals(raw, "Overlay", StringComparison.OrdinalIgnoreCase)) { mode = "Overlay"; return true; }
        mode = string.Empty;
        return false;
    }
}
