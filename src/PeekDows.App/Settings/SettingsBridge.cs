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
    private readonly SettingsService _settingsService;
    private readonly FileLogger? _logger;
    private readonly Func<string> _appVersionProvider;
    private readonly Action? _openSettingsFolder;
    private Func<System.Collections.Generic.IReadOnlyList<WindowRow>>? _windowsProvider;

    /// <summary>Raised when the UI reports its dirty state flipped (Save/Cancel footer).</summary>
    public event Action<bool>? DirtyChanged;

    public SettingsBridge(
        IPeekDowsController controller,
        SettingsService settingsService,
        FileLogger? logger = null,
        Func<string>? appVersionProvider = null,
        Action? openSettingsFolder = null,
        Func<System.Collections.Generic.IReadOnlyList<WindowRow>>? windowsProvider = null)
    {
        _controller = controller;
        _settingsService = settingsService;
        _logger = logger;
        _appVersionProvider = appVersionProvider ?? DefaultAppVersion;
        _openSettingsFolder = openSettingsFolder;
        _windowsProvider = windowsProvider;
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
    partial void ApplyIgnoredDraft(SettingsDraft draft, Core.Models.AppSettings settings, ref bool plainChanged);
    partial void ApplyHotkeysDraft(SettingsDraft draft, ref string? hotkeyError);

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
                case "dirtyChanged":
                    bool dirty = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;
                    DirtyChanged?.Invoke(dirty);
                    return null;
                case "requestWindows":
                    var windowsResponse = TryHandleWindowsMessage(type, root);
                    if (windowsResponse is not null) return windowsResponse;
                    return Error("windows-provider-missing");
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

        var settings = _controller.CurrentSettings;
        _logger?.Info("SettingsBridge: applying draft from settings UI");

        // 1. Toggle-backed keys — only when the value differs. Each controller toggle
        //    persists and raises its *Changed event so the tray menu stays in sync.
        if (draft.AutoArrange is { } av && settings.AutoArrange != av)
            _controller.ToggleAutoArrange();
        if (draft.Animate is { } nv && settings.AnimateWindowTransitions != nv)
            _controller.ToggleAnimateWindowTransitions();
        if (draft.DirectionalFocus is { } dv && settings.DirectionalFocusEnabled != dv)
            _controller.ToggleDirectionalFocus();
        if (draft.StartWithWindows is { } sv && settings.StartWithWindows != sv)
            _controller.ToggleStartWithWindows();
        if (draft.AllowRepositionMaximized is { } rv && settings.AllowRepositionMaximizedWindows != rv)
            _controller.ToggleAllowRepositionMaximizedWindows();

        // 2. Preset — SetWindowSizePreset persists AND triggers a re-arrange, so the
        //    on-screen layout matches the preview immediately.
        if (draft.Preset is { } presetName
            && TryParsePreset(presetName, out var preset)
            && _controller.CurrentWindowSizePreset != preset)
        {
            _controller.SetWindowSizePreset(preset);
        }

        // 3. Plain fields — mutate the shared instance, then ONE Save + OnSettingsChanged.
        bool plainChanged = false;
        if (draft.Enabled is { } ev && settings.Enabled != ev) { settings.Enabled = ev; plainChanged = true; }
        if (draft.ArrangeOnStartup is { } ov && settings.ArrangeOnStartup != ov) { settings.ArrangeOnStartup = ov; plainChanged = true; }
        if (draft.ShowTrayNotifications is { } tv && settings.ShowTrayNotifications != tv) { settings.ShowTrayNotifications = tv; plainChanged = true; }
        if (draft.ThresholdPx is { } th && th > 0 && settings.DirectionalFocusThresholdPx != th) { settings.DirectionalFocusThresholdPx = th; plainChanged = true; }

        ApplyIgnoredDraft(draft, settings, ref plainChanged);
        ApplyBetaDraft(draft, settings, ref plainChanged);
        string? hotkeyError = null;
        ApplyHotkeysDraft(draft, ref hotkeyError);
        if (hotkeyError is not null)
            return Applied(ok: false, error: hotkeyError);

        if (plainChanged)
        {
            _settingsService.Save(settings);
            _controller.OnSettingsChanged();
        }

        return Applied(ok: true, error: null);
    }

    private static bool TryParsePreset(string name, out WindowSizePreset preset)
        => Enum.TryParse(name, ignoreCase: true, out preset) && Enum.IsDefined(preset);

    private string Applied(bool ok, string? error)
    {
        // On success the fresh live snapshot rides along as the new source of truth —
        // this also absorbs controller-side corrections (e.g. StartWithWindows reverting
        // when the Startup shortcut could not be created).
        return JsonSerializer.Serialize(
            ok ? (object)new { type = "applied", ok, data = SnapshotData() }
               : new { type = "applied", ok, error },
            WriteOptions);
    }

    private static string Error(string code)
        => JsonSerializer.Serialize(new { type = "error", code }, WriteOptions);
}
