using System;
using System.Text.Json;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>
/// Pure JSON message protocol between the WebView2 settings UI and the live app.
/// Knows nothing about WebView2 or WinForms: HandleMessage takes a JSON string and
/// returns the JSON response to post back, or null when no response is expected.
/// Malformed or unknown input is logged and answered with an error payload — never throws.
/// Message types:
///   UI → app: apply{data}, requestSnapshot, openLogFile, openLogsFolder,
///             openSettingsFolder, dirtyChanged{value}
///   app → UI: settings{data}, externalChange{data}, applied{ok, data|error}, error{code}
/// </summary>
public sealed partial class SettingsBridge
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IPeekDowsController _controller;
    private readonly FileLogger? _logger;
    private readonly Func<string> _appVersionProvider;
    private readonly Action? _openSettingsFolder;
    private readonly Action<Action> _runDialog;
    private Func<System.Collections.Generic.IReadOnlyList<WindowRow>>? _windowsProvider;

    /// <summary>Raised when the UI reports its dirty state flipped (Save/Cancel footer).</summary>
    public event Action<bool>? DirtyChanged;

    public SettingsBridge(
        IPeekDowsController controller,
        FileLogger? logger = null,
        Func<string>? appVersionProvider = null,
        Action? openSettingsFolder = null,
        Func<System.Collections.Generic.IReadOnlyList<WindowRow>>? windowsProvider = null,
        Action<Action>? runDialog = null)
    {
        _controller = controller;
        _logger = logger;
        _appVersionProvider = appVersionProvider ?? DefaultAppVersion;
        _openSettingsFolder = openSettingsFolder;
        _windowsProvider = windowsProvider;
        _runDialog = runDialog ?? (action => action());
    }

    /// <summary>Injection alternative du provider Windows (utilisée par SettingsHostForm).</summary>
    public void SetWindowsProvider(Func<System.Collections.Generic.IReadOnlyList<WindowRow>> provider)
        => _windowsProvider = provider;

    // ----- hooks v2 (implémentés dans des fichiers partiels exclusifs) -----
    // Note: les hooks void sans implémentation sont des no-ops (pas d'erreur build).
    // Pour Windows on utilise un handler injectable (un partial non-void exigerait
    // une implémentation immédiate et casserait le build avant l'arrivée de l'Agent C).
    partial void AugmentSnapshotIgnored(System.Collections.Generic.Dictionary<string, object?> data);
    partial void AugmentSnapshotHotkeys(System.Collections.Generic.Dictionary<string, object?> data);
    partial void ApplyIgnoredDraft(SettingsDraft draft, Core.Models.AppSettings settings, ref bool changed);

    private Func<string, JsonElement, string?>? _windowsMessageHandler;

    /// <summary>Enregistre le handler du message requestWindows (fourni par l'Agent C, wiré par D).</summary>
    public void SetWindowsMessageHandler(Func<string, JsonElement, string?> handler)
        => _windowsMessageHandler = handler;

    private string? TryHandleWindowsMessage(string type, JsonElement root)
        => _windowsMessageHandler?.Invoke(type, root);

    private static string DefaultAppVersion()
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Serializes the live settings as a "settings" message for the UI.</summary>
    public string BuildSnapshotMessage()
        => JsonSerializer.Serialize(new { type = "settings", data = SnapshotData() }, WriteOptions);

    /// <summary>Serializes the live settings as an "externalChange" message (tray toggled meanwhile).</summary>
    public string BuildExternalChangeMessage()
        => JsonSerializer.Serialize(new { type = "externalChange", data = SnapshotData() }, WriteOptions);

    public string BuildRuntimeStatusMessage()
        => JsonSerializer.Serialize(new { type = "runtimeStatus", data = RuntimeStatus() }, WriteOptions);

    private object RuntimeStatus() => new { isPaused = _controller.IsPaused, pauseDescription = _controller.PauseDescription };

    private object SnapshotData()
    {
        var s = _controller.CurrentSettings;
        var data = new System.Collections.Generic.Dictionary<string, object?>(System.StringComparer.Ordinal)
        {
            ["enabled"] = s.Enabled,
            ["autoArrange"] = s.AutoArrange,
            ["animate"] = s.AnimateWindowTransitions,
            ["directionalFocus"] = s.DirectionalFocusEnabled,
            ["startWithWindows"] = s.StartWithWindows,
            ["allowRepositionMaximized"] = s.AllowRepositionMaximizedWindows,
            ["preset"] = s.WindowSizePreset.ToString(),
            ["arrangeOnStartup"] = s.ArrangeOnStartup,
            ["showTrayNotifications"] = s.ShowTrayNotifications,
            ["thresholdPx"] = s.DirectionalFocusThresholdPx,
            ["version"] = s.Version,
            ["appVersion"] = _appVersionProvider(),
            ["runtime"] = RuntimeStatus(),
        };
        AugmentSnapshotIgnored(data);
        AugmentSnapshotHotkeys(data);
        AugmentSnapshotBeta(data);
        return data;
    }

    /// <summary>
    /// Handles one message from the UI. Returns the JSON response to post back, or null
    /// when the message needs no response. Never throws.
    /// </summary>
    public string? HandleMessage(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"SettingsBridge: malformed message dropped: {ex.Message}");
            return Error("malformed-json");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeEl)
                || typeEl.GetString() is not { } type)
            {
                _logger?.Warn("SettingsBridge: message without a string 'type' dropped");
                return Error("missing-type");
            }

            switch (type)
            {
                case "apply":
                    return root.TryGetProperty("data", out var data)
                        ? ApplyDraft(data)
                        : Error("apply-without-data");
                case "requestSnapshot":
                    return BuildSnapshotMessage();
                case "openLogFile":
                    _controller.OpenLogFile();
                    return null;
                case "openLogsFolder":
                    _controller.OpenLogsFolder();
                    return null;
                case "openSettingsFolder":
                    _openSettingsFolder?.Invoke();
                    return null;
                case "openLayoutProfiles":
                    _runDialog(_controller.OpenLayoutProfiles);
                    return null;
                case "exportLayoutSettings":
                    _runDialog(_controller.ExportLayoutSettings);
                    return null;
                case "importLayoutSettings":
                    _runDialog(_controller.ImportLayoutSettings);
                    return null;
                case "previewArrange":
                    _runDialog(_controller.PreviewArrange);
                    return null;
                case "cycleZoneWindow":
                    _runDialog(_controller.CycleZoneWindow);
                    return null;
                case "pauseFor":
                    if (!root.TryGetProperty("duration", out var duration) || duration.ValueKind != JsonValueKind.String)
                        return Error("invalid-pause-duration");
                    switch (duration.GetString())
                    {
                        case "5": _controller.PauseFor(TimeSpan.FromMinutes(5)); break;
                        case "15": _controller.PauseFor(TimeSpan.FromMinutes(15)); break;
                        case "60": _controller.PauseFor(TimeSpan.FromHours(1)); break;
                        case "manual": _controller.PauseUntilResumed(); break;
                        default: return Error("invalid-pause-duration");
                    }
                    return null;
                case "resume":
                    _controller.Resume();
                    return null;
                case "dirtyChanged":
                    bool dirty = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;
                    DirtyChanged?.Invoke(dirty);
                    return null;
                case "requestWindows":
                    var windowsResponse = TryHandleWindowsMessage(type, root);
                    if (windowsResponse is not null) return windowsResponse;
                    return Error("windows-provider-missing");
                case "pickSoundFile":
                case "previewSound":
                case "deleteSoundFile":
                    return TryHandleSoundMessage(type, root);
                default:
                    var ext = TryHandleWindowsMessage(type, root);
                    if (ext is not null) return ext;
                    _logger?.Info($"SettingsBridge: unknown message type '{type}' ignored");
                    return Error($"unknown-type:{type}");
            }
        }
    }

    private string ApplyDraft(JsonElement data)
    {
        SettingsDraft? draft;
        try
        {
            draft = data.Deserialize<SettingsDraft>(ReadOptions);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"SettingsBridge: malformed apply payload dropped: {ex.Message}");
            return Applied(ok: false, error: "malformed-draft");
        }
        if (draft is null)
        {
            _logger?.Warn("SettingsBridge: apply payload deserialized to null");
            return Applied(ok: false, error: "empty-draft");
        }

        var previous = _controller.CurrentSettings;
        var next = previous.CopyForSettingsEdit();
        _logger?.Info("SettingsBridge: applying draft from settings UI");

        var hotkeyError = ApplyHotkeysDraft(draft, next, out var changed);
        if (hotkeyError is not null)
            return Applied(ok: false, error: hotkeyError);

        if (draft.Enabled is { } enabled) next.Enabled = enabled;
        if (draft.AutoArrange is { } autoArrange) next.AutoArrange = autoArrange;
        if (draft.Animate is { } animate) next.AnimateWindowTransitions = animate;
        if (draft.DirectionalFocus is { } focus) next.DirectionalFocusEnabled = focus;
        if (draft.StartWithWindows is { } startup) next.StartWithWindows = startup;
        if (draft.AllowRepositionMaximized is { } reposition) next.AllowRepositionMaximizedWindows = reposition;
        if (draft.Preset is { } presetName && TryParsePreset(presetName, out var preset))
            next.WindowSizePreset = preset;
        if (draft.ArrangeOnStartup is { } arrangeOnStartup) next.ArrangeOnStartup = arrangeOnStartup;
        if (draft.ShowTrayNotifications is { } notifications) next.ShowTrayNotifications = notifications;
        if (draft.ThresholdPx is > 0 and var threshold) next.DirectionalFocusThresholdPx = threshold;

        changed |= previous.Enabled != next.Enabled
            || previous.AutoArrange != next.AutoArrange
            || previous.AnimateWindowTransitions != next.AnimateWindowTransitions
            || previous.DirectionalFocusEnabled != next.DirectionalFocusEnabled
            || previous.StartWithWindows != next.StartWithWindows
            || previous.AllowRepositionMaximizedWindows != next.AllowRepositionMaximizedWindows
            || previous.WindowSizePreset != next.WindowSizePreset
            || previous.ArrangeOnStartup != next.ArrangeOnStartup
            || previous.ShowTrayNotifications != next.ShowTrayNotifications
            || previous.DirectionalFocusThresholdPx != next.DirectionalFocusThresholdPx;

        ApplyIgnoredDraft(draft, next, ref changed);
        ApplyBetaDraft(draft, next, ref changed);

        return !changed || _controller.TryCommitSettings(next, out var error)
            ? Applied(ok: true, error: null)
            : Applied(ok: false, error: error);
    }

    private static bool TryParsePreset(string name, out WindowSizePreset preset)
        => Enum.TryParse(name, ignoreCase: true, out preset) && Enum.IsDefined(preset);

    private string Applied(bool ok, string? error)
    {
        // The fresh live snapshot becomes the UI's baseline after a successful save.
        return JsonSerializer.Serialize(
            ok ? (object)new { type = "applied", ok, data = SnapshotData() }
               : new { type = "applied", ok, error },
            WriteOptions);
    }

    private static string Error(string code)
        => JsonSerializer.Serialize(new { type = "error", code }, WriteOptions);
}
