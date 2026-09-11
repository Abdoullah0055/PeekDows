using System;
using System.Text.Json;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>
/// Editable draft payload sent by the settings UI on Save. Nullable keys are "untouched".
/// JSON is camelCase; PropertyNameCaseInsensitive accepts either casing.
/// </summary>
public sealed class SettingsDraft
{
    public bool? Enabled { get; set; }
    public bool? AutoArrange { get; set; }
    public bool? Animate { get; set; }
    public bool? DirectionalFocus { get; set; }
    public bool? StartWithWindows { get; set; }
    public bool? AllowRepositionMaximized { get; set; }
    public string? Preset { get; set; }
    public bool? ArrangeOnStartup { get; set; }
    public bool? ShowTrayNotifications { get; set; }
    public int? ThresholdPx { get; set; }
}

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
public sealed class SettingsBridge
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IPeekDowsController _controller;
    private readonly SettingsService _settingsService;
    private readonly FileLogger? _logger;
    private readonly Func<string> _appVersionProvider;
    private readonly Action? _openSettingsFolder;

    /// <summary>Raised when the UI reports its dirty state flipped (Save/Cancel footer).</summary>
    public event Action<bool>? DirtyChanged;

    public SettingsBridge(
        IPeekDowsController controller,
        SettingsService settingsService,
        FileLogger? logger = null,
        Func<string>? appVersionProvider = null,
        Action? openSettingsFolder = null)
    {
        _controller = controller;
        _settingsService = settingsService;
        _logger = logger;
        _appVersionProvider = appVersionProvider ?? DefaultAppVersion;
        _openSettingsFolder = openSettingsFolder;
    }

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
        return new
        {
            enabled = s.Enabled,
            autoArrange = s.AutoArrange,
            animate = s.AnimateWindowTransitions,
            directionalFocus = s.DirectionalFocusEnabled,
            startWithWindows = s.StartWithWindows,
            allowRepositionMaximized = s.AllowRepositionMaximizedWindows,
            preset = s.WindowSizePreset.ToString(),
            arrangeOnStartup = s.ArrangeOnStartup,
            showTrayNotifications = s.ShowTrayNotifications,
            thresholdPx = s.DirectionalFocusThresholdPx,
            version = s.Version,
            appVersion = _appVersionProvider()
        };
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
                default:
                    _logger?.Info($"SettingsBridge: unknown message type '{type}' ignored");
                    return Error($"unknown-type:{type}");
            }
        }
    }

    private string ApplyDraft(JsonElement data)
    {
        // Implemented in Task 3; the apply tests drive it.
        throw new NotImplementedException("apply lands in Task 3");
    }

    private static string Error(string code)
        => JsonSerializer.Serialize(new { type = "error", code }, WriteOptions);
}
