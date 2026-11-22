using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PeekDows.App.AutoArrange;
using PeekDows.App.Animation;
using PeekDows.App.Diagnostics;
using PeekDows.App.Focus;
using PeekDows.App.Hotkeys;
using PeekDows.App.Sound;
using PeekDows.App.Startup;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App;

public partial class PeekDowsAppContext : ApplicationContext, IPeekDowsController
{
    private readonly SettingsService _settingsService;
    private readonly WindowDiscoveryService _discoveryService;
    private readonly WindowClassifier _classifier;
    private readonly MonitorService _monitorService;
    private readonly LayoutEngine _layoutEngine;
    private readonly WindowPlacementService _placementService;
    private readonly HotkeyService _hotkeyService;
    private readonly TrayIconController _trayController;
    private readonly FileLogger _logger;
    private readonly AutoArrangeService _autoArrangeService;
    private readonly MultiMonitorLayoutService _multiMonitorLayoutService;
    private readonly IStartupService _startupService;
    private readonly PauseStateService _pauseState;
    private readonly System.Windows.Forms.Timer _pauseCheckTimer;
    private readonly DirectionalFocusService _directionalFocusService;
    private readonly ForegroundWatchService _kbWatchService;
    private readonly DirectionalFocusRegistry _directionalFocusRegistry;
    private readonly DirectionalFocusLayoutSnapshotService _directionalFocusSnapshotService;
    private readonly VirtualDesktopService _virtualDesktopService;
    private readonly WindowActivationService _windowActivationService;
    private readonly UnstableWindowTracker _unstableWindowTracker;
    private readonly WindowAnimationService _animationService;
    private readonly LoneWindowMaximizePolicy _loneWindowMaximizePolicy;
    private readonly ArrangeHistoryService _historyService;
    private readonly PeekDows.App.Sound.ArrangeSoundService _soundService;
    private readonly System.Windows.Forms.Timer _soundAutoOffTimer;
    private DateTime? _soundEnabledUntil;
    private int _arrangeInProgress;
    private int _placementGeneration;
    private bool _isPreviewOpen;

    private AppSettings _settings;

    // Focus hint overlay (Beta): created lazily on the UI thread, driven by
    // DirectionalFocusService gesture events. Never focusable/clickable.
    private HintOverlayForm? _hintOverlay;
    private WindowIconService _iconService;
    private System.Drawing.Point _hintAnchor = System.Drawing.Point.Empty;
    private Rect _hintAnchorMonitor;

    public RuntimeState State => _pauseState.State;
    public bool IsPaused => _pauseState.IsPaused;
    public DateTimeOffset? PauseUntil => _pauseState.PauseUntil;
    public string? PauseDescription => _pauseState.GetPauseDescription();

    public AppSettings CurrentSettings => _settings;

    public bool IsAutoArrangeRunning => _autoArrangeService.IsRunning;

    public bool IsStartWithWindowsEnabled => _settings.StartWithWindows;

    public bool IsDirectionalFocusEnabled => _settings.DirectionalFocusEnabled;

    public bool AllowRepositionMaximizedWindows => _settings.AllowRepositionMaximizedWindows;

    public WindowSizePreset CurrentWindowSizePreset => _settings.WindowSizePreset;

    public event Action<RuntimeState>? StateChanged;
    public event Action<bool>? AutoArrangeChanged;
    public event Action<bool>? StartWithWindowsChanged;
    public event Action<bool>? DirectionalFocusChanged;
    public event Action<bool>? AnimateWindowTransitionsChanged;
    public event Action<bool>? AllowRepositionMaximizedWindowsChanged;
    public event Action<WindowSizePreset>? WindowSizePresetChanged;
    public event Action<bool>? SoundChanged;
    public event Action<string>? LayoutModeChanged;

    public string LogFilePath => _logger.LogFilePath;

    public PeekDowsAppContext()
    {
        _logger = new FileLogger();
        _logger.Info("PeekDows starting");

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _logger.Info($"Settings loaded: path={_settingsService.GetType().GetProperty("SettingsFilePath")?.GetValue(_settingsService) ?? "N/A"}, version={_settings.Version}");
        _logger.Info($"DirectionalFocusThresholdPx loaded: value={_settings.DirectionalFocusThresholdPx}");
        _logger.Info($"Settings: Enabled={_settings.Enabled}");
        _logger.Info($"Settings: AutoArrange={_settings.AutoArrange}");
        _logger.Info($"Settings: AllowRepositionMaximizedWindows={_settings.AllowRepositionMaximizedWindows}");
        _logger.Info($"Settings: AnimateWindowTransitions={_settings.AnimateWindowTransitions}");
        _logger.Info($"Settings: IgnoredProcesses.Count={_settings.IgnoredProcesses.Count}");
        _logger.Info($"Settings: IgnoredClasses.Count={_settings.IgnoredClasses.Count}");

        _discoveryService = new WindowDiscoveryService();
        _virtualDesktopService = new VirtualDesktopService(_logger);
        _classifier = new WindowClassifier(_settings, hwnd => _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd));
        _monitorService = new MonitorService(_logger);
        _layoutEngine = new LayoutEngine();
        _loneWindowMaximizePolicy = new LoneWindowMaximizePolicy(_logger);
        _multiMonitorLayoutService = new MultiMonitorLayoutService(_monitorService, _layoutEngine, _loneWindowMaximizePolicy, _logger);
        // One shared tracker: placements, activation and the ArrangeNow circuit breaker all
        // consult it so a window that hung/blocked once is skipped everywhere until it settles.
        _unstableWindowTracker = new UnstableWindowTracker(_logger);
        _animationService = new WindowAnimationService(new Win32AnimationApi(), _unstableWindowTracker, _logger);
        _placementService = new WindowPlacementService(
            _logger, _unstableWindowTracker, _animationService, () => _settings.AnimateWindowTransitions);

        _pauseState = new PauseStateService();
        _pauseState.StateChanged += OnPauseStateChanged;

        _pauseCheckTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pauseCheckTimer.Tick += OnPauseCheckTimerTick;

        _logger.Info("Hotkey registration started");
        _hotkeyService = new HotkeyService(_logger);
        _hotkeyService.ArrangeNowRequested += OnArrangeNowRequested;
        _hotkeyService.PauseResumeRequested += OnPauseResumeRequested;
        _hotkeyService.UndoRequested += OnUndoArrangeRequested;
        _historyService = new ArrangeHistoryService(_logger, maximizePolicy: _loneWindowMaximizePolicy);

        RegisterConfiguredHotkeys();

        if (!_hotkeyService.RegisterUndoHotkey())
        {
            _logger.Warn("Failed to register Ctrl+Win+Z hotkey. It may already be in use.");
        }

        _trayController = new TrayIconController(this, _logger, _settingsService);
        _logger.Info("Tray initialized");

        _startupService = new StartupService(_logger);
        SyncStartWithWindows();

        _autoArrangeService = new AutoArrangeService(
            _discoveryService, _classifier, this, _logger, () => _isPreviewOpen);

        if (_settings.AutoArrange)
        {
            _autoArrangeService.Start(_settings);
        }

        _directionalFocusRegistry = new DirectionalFocusRegistry(
            IsDirectionalFocusWindowAvailable,
            hwnd => _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd),
            _logger);
        var gestureDetector = new DirectionalFocusGestureDetector();
        _windowActivationService = new WindowActivationService(new Win32ActivationApi(new Win32InputSimulator(_logger)), _logger, _unstableWindowTracker);

        _directionalFocusSnapshotService = new DirectionalFocusLayoutSnapshotService(
            _monitorService,
            GetDirectionalFocusWindowRect,
            hwnd => IsDirectionalFocusWindowAvailable(hwnd) && _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd),
            GetDirectionalFocusSlotRects,
            logger: _logger);

        _directionalFocusService = new DirectionalFocusService(
            gestureDetector,
            _directionalFocusRegistry,
            _monitorService,
            _windowActivationService,
            _virtualDesktopService,
            _directionalFocusSnapshotService,
            GetDirectionalFocusCandidateWindows,
            () => _settings.DirectionalFocusThresholdPx,
            _logger,
            _animationService);

        _directionalFocusService.GestureStarted += OnFocusHintGestureStarted;
        _directionalFocusService.GestureUpdated += OnFocusHintGestureUpdated;
        _directionalFocusService.GestureEnded += OnFocusHintGestureEnded;

        // Window icon cache for the hint overlay wedges (Agent B). Owned here,
        // disposed with the context; Bitmaps handed to the overlay stay owned
        // by the service and are never disposed by the overlay.
        _iconService = new WindowIconService(logger: _logger);

        if (_settings.DirectionalFocusEnabled)
        {
            _directionalFocusService.Start();
        }

    // Layout/flip + Taskmgr tracer: always active, even when Directional
    // Focus is off, so layout flips stay explainable in all configurations.
    // Polling-only design: no keyboard hook is installed anymore.
    _kbWatchService = new ForegroundWatchService(_logger);
        _kbWatchService.Start();

        _soundService = new PeekDows.App.Sound.ArrangeSoundService(GetUserSoundsDir, _logger);
        _soundAutoOffTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _soundAutoOffTimer.Tick += OnSoundAutoOffTick;
        RefreshArrangeSoundAutoOff();

        if (_settings.ArrangeOnStartup)
        {
            _logger.Info("ArrangeOnStartup=true, triggering initial arrange");
            ArrangeNow();
        }

        _logger.Info("PeekDows ready");
    }

    public void ArrangeNow()
    {
        _logger.Info("ArrangeNow requested");

        if (_pauseState.IsPaused)
        {
            _logger.Info("ArrangeNow ignored because app is paused");
            return;
        }

        if (!_settings.Enabled)
        {
            _logger.Info("ArrangeNow ignored because settings.Enabled is false");
            return;
        }

        ExecuteArrange();
    }

    public void TogglePause()
    {
        if (_pauseState.IsPaused)
        {
            _pauseState.TogglePause();
            StopPauseCheckTimer();
        }
        else
        {
            _pauseState.TogglePause();
        }
    }

    public void PauseUntilResumed()
    {
        _logger.Info("Pause enabled until resumed");
        _pauseState.PauseUntilResumed();
        StopPauseCheckTimer();
    }

    public void PauseFor(TimeSpan duration)
    {
        if (duration == TimeSpan.FromMinutes(5))
            _logger.Info("Pause enabled for 5 minutes");
        else if (duration == TimeSpan.FromMinutes(15))
            _logger.Info("Pause enabled for 15 minutes");
        else if (duration == TimeSpan.FromHours(1))
            _logger.Info("Pause enabled for 1 hour");
        else
            _logger.Info($"Pause enabled for {duration.TotalMinutes} minutes");

        _pauseState.PauseFor(duration);
        StartPauseCheckTimerIfNeeded();
    }

    public void Resume()
    {
        _logger.Info("Pause disabled, resumed");
        _pauseState.Resume();
        StopPauseCheckTimer();
    }

    private void StopPauseCheckTimer()
    {
        if (_pauseCheckTimer.Enabled)
        {
            _pauseCheckTimer.Stop();
            _logger.Info("Pause check timer stopped");
        }
    }

    private void StartPauseCheckTimerIfNeeded()
    {
        if (_pauseState.PauseUntil != null && !_pauseCheckTimer.Enabled)
        {
            _pauseCheckTimer.Start();
            _logger.Info("Pause check timer started");
        }
    }

    private void OnPauseCheckTimerTick(object? sender, EventArgs e)
    {
        if (_pauseState.CheckExpired(DateTimeOffset.Now))
        {
            StopPauseCheckTimer();
            _logger.Info("Pause expired, resuming");
            return;
        }

        if (!_pauseState.IsPaused)
        {
            StopPauseCheckTimer();
        }
    }

    private void OnPauseStateChanged(RuntimeState state)
    {
        if (state == RuntimeState.Paused)
        {
            _placementGeneration++;
            // Cancellation rule 4 (spec): pausing lands every in-flight tween on its target
            // instantly so nothing keeps moving while paused.
            _animationService.SnapAllToTarget();
        }
        StateChanged?.Invoke(state);
    }

    private void OnPauseResumeRequested()
    {
        TogglePause();
    }

    public void ToggleAutoArrange()
    {
        _settings.AutoArrange = !_settings.AutoArrange;
        _settingsService.Save(_settings);
        _logger.Info($"AutoArrange toggled to {_settings.AutoArrange}");

        if (_settings.AutoArrange)
        {
            _autoArrangeService.Start(_settings);
        }
        else
        {
            _autoArrangeService.Stop();
        }

        AutoArrangeChanged?.Invoke(_settings.AutoArrange);
    }

    public void ToggleStartWithWindows()
    {
        _settings.StartWithWindows = !_settings.StartWithWindows;
        _settingsService.Save(_settings);
        _logger.Info($"StartWithWindows toggled to {_settings.StartWithWindows}");

        var success = _startupService.SetEnabled(_settings.StartWithWindows);
        if (_settings.StartWithWindows && !success)
        {
            _logger.Warn("StartWithWindows toggle: Enable failed, reverting setting");
            _settings.StartWithWindows = false;
            _settingsService.Save(_settings);
        }

        StartWithWindowsChanged?.Invoke(_settings.StartWithWindows);
    }

    public void ToggleDirectionalFocus()
    {
        _settings.DirectionalFocusEnabled = !_settings.DirectionalFocusEnabled;
        _settingsService.Save(_settings);
        _logger.Info($"DirectionalFocus toggled to {_settings.DirectionalFocusEnabled}");

        if (_settings.DirectionalFocusEnabled)
        {
            _directionalFocusService.Start();
        }
        else
        {
            _directionalFocusService.Stop();
        }

        DirectionalFocusChanged?.Invoke(_settings.DirectionalFocusEnabled);
    }

    /// <summary>
    /// Sets the direction-hint mode (Off/Overlay). Normalizes null/unknown to
    /// "Overlay" (legacy "Both"→Overlay, "Spotlight"→Off), persists via
    /// SettingsService, then notifies runtime. Menu refresh is builder-side
    /// (radio checks updated on Click + DropDownOpening), no full menu rebuild.
    /// Raises <see cref="FocusHintModeChanged"/> so the open settings window can
    /// mirror the tray change live (same pattern as the other Changed events).
    /// </summary>
    public event Action<string>? FocusHintModeChanged;

    public void SetFocusHintMode(string mode)
    {
        var normalized = NormalizeFocusHintMode(mode);
        if (string.Equals(_settings.FocusHintMode, normalized, StringComparison.Ordinal))
            return;

        _settings.FocusHintMode = normalized;
        _settingsService.Save(_settings);
        _logger.Info($"FocusHintMode changed: {normalized}");
        OnSettingsChanged();
        FocusHintModeChanged?.Invoke(normalized);
    }

    /// <summary>
    /// Toggles Off ↔ Overlay. Convenience for a future hotkey; tray submenu
    /// calls SetFocusHintMode directly.
    /// </summary>
    public void CycleFocusHintMode()
    {
        SetFocusHintMode(NormalizeFocusHintMode(_settings.FocusHintMode) == "Off" ? "Overlay" : "Off");
    }

    private static string NormalizeFocusHintMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "Overlay";
        switch (mode.Trim().ToLowerInvariant())
        {
            case "off": return "Off";
            case "overlay": return "Overlay";
            case "spotlight": return "Off";
            case "both": return "Overlay";
            default: return "Overlay";
        }
    }

    /// <summary>
    /// Sets the layout mode (Grid|Halves|Thirds). Normalizes null/unknown to
    /// "Grid", persists via SettingsService, re-arranges immediately (like
    /// SetWindowSizePreset — otherwise windows keep stale positions from the
    /// previous mode, showing gaps and overlaps), then notifies runtime.
    /// Raises <see cref="LayoutModeChanged"/> so the open settings window can
    /// mirror the tray change live (same pattern as the other Changed events).
    /// </summary>
    public void SetLayoutMode(string mode)
    {
        var normalized = NormalizeLayoutMode(mode);
        if (string.Equals(_settings.LayoutMode, normalized, StringComparison.Ordinal))
            return;

        _settings.LayoutMode = normalized;
        _settingsService.Save(_settings);
        _placementGeneration++;
        _logger.Info($"LayoutMode changed: {normalized}");
        OnSettingsChanged();
        LayoutModeChanged?.Invoke(normalized);
        ArrangeNow();
    }

    public bool CanUndoArrange
    {
        get
        {
            try { return _historyService.CanUndo; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Restores window positions from before the last arrange (F1).
    /// </summary>
    public void UndoArrange()
    {
        try
        {
            _logger.Info("UndoArrange requested");
            if (!_historyService.CanUndo) return;
            _placementGeneration++;
            _stackedZones = [];
            _historyService.Undo(_animationService.Cancel);
            _directionalFocusService.InvalidateLayout();
        }
        catch (Exception ex)
        {
            _logger.Warn($"UndoArrange failed: {ex.Message}");
        }
    }

    private static string NormalizeLayoutMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "Grid";
        switch (mode.Trim().ToLowerInvariant())
        {
            case "grid":
            case "g":
                return "Grid";
            case "halves":
            case "h":
                return "Halves";
            case "thirds":
            case "t":
                return "Thirds";
            case "custom":
                return "Custom";
            default: return "Grid";
        }
    }

    public bool IsAnimateWindowTransitionsEnabled => _settings.AnimateWindowTransitions;

    public void ToggleAnimateWindowTransitions()
    {
        _settings.AnimateWindowTransitions = !_settings.AnimateWindowTransitions;
        _settingsService.Save(_settings);
        _logger.Info($"AnimateWindowTransitions toggled to {_settings.AnimateWindowTransitions}");

        if (!_settings.AnimateWindowTransitions)
        {
            // Turning animation off mid-flight: land every tween instantly rather than
            // letting them keep running with the engine that is being switched off.
            _animationService.SnapAllToTarget();
        }

        AnimateWindowTransitionsChanged?.Invoke(_settings.AnimateWindowTransitions);
    }

    public void ToggleAllowRepositionMaximizedWindows()
    {
        _settings.AllowRepositionMaximizedWindows = !_settings.AllowRepositionMaximizedWindows;
        _settingsService.Save(_settings);
        _placementGeneration++;
        _logger.Info($"AllowRepositionMaximizedWindows changed: {_settings.AllowRepositionMaximizedWindows}");

        AllowRepositionMaximizedWindowsChanged?.Invoke(_settings.AllowRepositionMaximizedWindows);
    }

    public void SetWindowSizePreset(WindowSizePreset preset)
    {
        if (_settings.WindowSizePreset == preset) return;

        _settings.WindowSizePreset = preset;
        _settingsService.Save(_settings);
        _placementGeneration++;
        _directionalFocusService.InvalidateLayout();
        _logger.Info($"WindowSizePreset changed: {preset} = {LayoutEngine.GetPresetRatio(preset):P0}");
        WindowSizePresetChanged?.Invoke(preset);
        ArrangeNow();
    }

    // ----- Arrange sound (Beta): chime on each successful arrange -----

    private static string GetUserSoundsDir()
        => System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PeekDows", "sounds");


    public void ToggleArrangeSound()
    {
        _settings.ArrangeSoundEnabled = !_settings.ArrangeSoundEnabled;
        _settingsService.Save(_settings);
        _logger.Info($"ArrangeSound toggled to {_settings.ArrangeSoundEnabled}");
        OnSettingsChanged();
        SoundChanged?.Invoke(_settings.ArrangeSoundEnabled);
        RefreshArrangeSoundAutoOff();
    }

    /// <summary>
    /// Single arming point for the auto-off deadline, derived from the live
    /// settings: enabled + minutes > 0 → deadline armed; otherwise stopped.
    /// Called after ctor load, toggles and successful settings applies.
    /// </summary>
    public void RefreshArrangeSoundAutoOff()
    {
        try
        {
            if (_settings.ArrangeSoundEnabled && _settings.ArrangeSoundAutoOffMinutes > 0)
            {
                _soundEnabledUntil = PeekDows.Core.Services.ArrangeSoundPlanning.ComputeAutoOffDeadline(
                    DateTime.UtcNow, _settings.ArrangeSoundAutoOffMinutes);
                _soundAutoOffTimer.Start();
                _logger.Info($"ArrangeSound auto-off armed: {_settings.ArrangeSoundAutoOffMinutes}min (until={_soundEnabledUntil?.ToString("o")})");
            }
            else
            {
                _soundEnabledUntil = null;
                _soundAutoOffTimer.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"ArrangeSound auto-off refresh failed: {ex.Message}");
        }
    }

    public void DisableArrangeSound(string reason)
    {
        if (!_settings.ArrangeSoundEnabled) return;
        _settings.ArrangeSoundEnabled = false;
        _soundEnabledUntil = null;
        _soundAutoOffTimer.Stop();
        _settingsService.Save(_settings);
        _logger.Info($"ArrangeSound disabled ({reason})");
        OnSettingsChanged();
        SoundChanged?.Invoke(false);
    }

    private void OnSoundAutoOffTick(object? sender, EventArgs e)
    {
        try
        {
            if (_soundEnabledUntil.HasValue && DateTime.UtcNow >= _soundEnabledUntil.Value)
                DisableArrangeSound("auto-off");
        }
        catch (Exception ex)
        {
            _logger.Warn($"ArrangeSound auto-off check failed: {ex.Message}");
        }
    }

    public string[] GetUserSounds()
    {
        try
        {
            var dir = GetUserSoundsDir();
            if (!System.IO.Directory.Exists(dir))
                return Array.Empty<string>();
            var files = new System.Collections.Generic.List<string>();
            foreach (var pattern in new[] { "*.mp3", "*.wav" })
            {
                foreach (var f in System.IO.Directory.GetFiles(dir, pattern))
                    files.Add(System.IO.Path.GetFileName(f));
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public string PickArrangeSoundFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Audio|*.mp3;*.wav",
            Multiselect = false,
        };
        if (dlg.ShowDialog() != DialogResult.OK) return "";

        var source = dlg.FileName;
        if (!SoundFileValidator.HasSupportedExtension(source))
            throw new InvalidDataException("Unsupported sound file extension.");
        if (new FileInfo(source).Length > SoundFileValidator.MaxBytes)
            throw new InvalidDataException("Sound file exceeds the size limit.");
        var clean = SoundFileValidator.SanitizeFileName(Path.GetFileName(source));
        if (string.IsNullOrEmpty(clean))
            throw new InvalidDataException("Invalid sound file name.");

        var dir = GetUserSoundsDir();
        Directory.CreateDirectory(dir);
        var name = SoundFileValidator.Deduplicate(dir, clean);
        File.Copy(source, Path.Combine(dir, name));
        _logger.Info($"ArrangeSound: imported {name}");
        return name;
    }

    public void DeleteArrangeSoundFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !SoundFileValidator.HasSupportedExtension(name))
            throw new InvalidDataException("Invalid custom sound file name.");
        if (string.Equals(Path.GetExtension(name), ".wav", StringComparison.OrdinalIgnoreCase)
            && ArrangeSoundService.IsFactorySound(Path.GetFileNameWithoutExtension(name)))
            throw new InvalidDataException("Factory sounds cannot be deleted.");

        var path = Path.Combine(GetUserSoundsDir(), name);
        if (!string.Equals(_settings.ArrangeSoundName, name, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            return;
        }

        var next = _settings.CopyForSettingsEdit();
        next.ArrangeSoundName = "chime-soft";
        DeleteSelectedSound(path, () =>
        {
            if (!TryCommitSettings(next, out var error))
                throw new IOException($"Could not reset the selected sound: {error}");
        }, _logger.Error);
        _logger.Info("ArrangeSound: deleted selected sound, reset to chime-soft");
    }

    internal static void DeleteSelectedSound(string path, Action persistFallback, Action<string, Exception> log)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".sound-delete-{Guid.NewGuid():N}.tmp");
        File.Move(path, temporaryPath);
        try
        {
            persistFallback();
        }
        catch (Exception saveError)
        {
            try { File.Move(temporaryPath, path); }
            catch (Exception restoreError)
            {
                throw new IOException($"Could not restore the selected sound. Recovery copy: {temporaryPath}",
                    new AggregateException(saveError, restoreError));
            }
            throw;
        }

        try { File.Delete(temporaryPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"ArrangeSound: could not remove temporary sound copy {temporaryPath}", ex);
        }
    }

    public void PreviewArrangeSound(string name)
    {
        try { _soundService.Play(name); } catch { }
    }

    // When StartWithWindows is false, do not delete an existing shortcut here.
    // The shortcut is removed only when the user explicitly disables the option from the tray.
    private void SyncStartWithWindows()
    {
        try
        {
            if (_settings.StartWithWindows)
            {
                if (!_startupService.IsEnabled())
                {
                    _logger.Info("StartWithWindows setting is true but shortcut missing, recreating");

                    if (!_startupService.Enable())
                    {
                        _logger.Warn("Failed to recreate startup shortcut, updating setting to false");
                        _settings.StartWithWindows = false;
                        _settingsService.Save(_settings);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to sync StartWithWindows", ex);
        }
    }

    public void OpenSettings()
    {
        _trayController.OpenSettings();
    }

    public void Exit()
    {
        _logger.Info("PeekDows exiting");
        try { _animationService.SnapAllToTarget(); } catch { }
            HideFocusHints();
            try { _iconService.Dispose(); } catch { }
            try { _kbWatchService.Dispose(); } catch { }
        _pauseCheckTimer.Stop();
        _pauseCheckTimer.Dispose();
        try { _soundAutoOffTimer.Stop(); } catch { }
        try { _soundAutoOffTimer.Dispose(); } catch { }
        try { _soundService.Dispose(); } catch { }
        _autoArrangeService.Dispose();
        _directionalFocusService.Dispose();
        _animationService.Dispose();
        _hotkeyService.Dispose();
        _trayController.Dispose();
        Application.Exit();
    }

    public void OpenLogFile()
    {
        try
        {
            if (!System.IO.File.Exists(_logger.LogFilePath))
            {
                _logger.Info("Log file created on demand");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _logger.LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to open log file", ex);
        }
    }

    public void OpenLogsFolder()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_logger.LogFilePath)!;
            if (!System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to open logs folder", ex);
        }
    }

    public void OnSettingsChanged()
    {
        _classifier.UpdateSettings(_settings);
        _directionalFocusService.InvalidateLayout();
        if (string.Equals(_settings.FocusHintMode, "Off", StringComparison.OrdinalIgnoreCase)
            || !_settings.DirectionalFocusEnabled)
        {
            HideFocusHints();
        }
        _logger.Info($"Settings changed at runtime: IgnoredProcesses.Count={_settings.IgnoredProcesses.Count}, IgnoredClasses.Count={_settings.IgnoredClasses.Count}, AllowRepositionMaximizedWindows={_settings.AllowRepositionMaximizedWindows}");
    }

    /// <summary>
    /// Ctrl+Win hold started: show the arrows overlay at the anchor.
    /// Runs on the UI thread (service timer thread).
    /// </summary>
    private void OnFocusHintGestureStarted(System.Drawing.Point anchor, Rect monitor)
    {
        try
        {
            _hintAnchor = anchor;
            _hintAnchorMonitor = monitor;
            EnsureHintForms();
            // First paint: arrows for populated slots (no active slot yet).
            OnFocusHintGestureUpdated(null, _settings.DirectionalFocusThresholdPx, 0);
            _kbWatchService.OnGestureStarted();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Focus hint start failed: {ex.Message}");
        }
    }

    private void OnFocusHintGestureUpdated(DirectionalFocusSlot? slot, int threshold, double distance)
    {
        try
        {
            if (!_settings.DirectionalFocusEnabled)
            {
                HideFocusHints();
                return;
            }
            EnsureHintForms();

            var available = _directionalFocusRegistry.GetAvailableSlots(_hintAnchorMonitor);
            var populated = new System.Collections.Generic.List<(DirectionalFocusSlot Slot, System.Drawing.Bitmap? Icon, string Label)>(available.Count);
            foreach (var (s, h) in available)
            {
                try
                {
                    if (_settings.OverlayShowAppIcons)
                    {
                        var (bmp, label) = _iconService.GetEntry(h);
                        populated.Add((s, bmp, label));
                    }
                    else
                    {
                        populated.Add((s, null, ""));
                    }
                }
                catch
                {
                    populated.Add((s, null, ""));
                }
            }

            bool hasTarget = slot != null && available.Any(a => a.Slot == slot.Value);
            var vm = FocusHintState.Resolve(_settings.FocusHintMode, slot, hasTarget);

            // Arrows-only overlay: show solely populated slots, no background, no text.
            // Nothing populated → hide (nothing to point at).
            if (vm.ShowOverlay && _hintOverlay != null && populated.Count > 0)
            {
                _hintOverlay.ShowAt(_hintAnchor, populated, slot);
            }
            else
            {
                HideFocusHints();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Focus hint update failed: {ex.Message}");
        }
    }

    private void OnFocusHintGestureEnded()
    {
        try
        {
            _kbWatchService.OnGestureEnded();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Focus hint gesture-end trace failed: {ex.Message}");
        }
        HideFocusHints();
    }

    private void EnsureHintForms()
    {
        if (_hintOverlay == null)
            _hintOverlay = new HintOverlayForm(msg => _logger.Info($"HintOverlay: {msg}"));
    }

    private void HideFocusHints()
    {
        try { _hintOverlay?.HideHint(); } catch { }
    }

    private void OnArrangeNowRequested()
    {
        ArrangeNow();
    }

    private void OnUndoArrangeRequested()
    {
        UndoArrange();
    }

    /// <summary>
    /// Returns the hwnds of currently eligible windows (visible, on the current
    /// virtual desktop, not a system/shell window). Used by Directional Focus to
    /// rebuild the slot map on demand without moving anything.
    /// </summary>
    private IReadOnlyList<IntPtr> GetDirectionalFocusCandidateWindows()
    {
        // A desktop switch can occur within the virtual desktop cache's TTL.
        _virtualDesktopService.InvalidateCache();
        var rawWindows = _discoveryService.GetDirectionalFocusWindows();
        var result = new List<IntPtr>(rawWindows.Count);
        var rulesByMonitor = new Dictionary<IntPtr, List<AppRule>>();
        foreach (var raw in rawWindows)
        {
            if (!_classifier.IsEligibleForDirectionalFocus(raw)) continue;

            var monitor = _monitorService.GetMonitorForWindow(raw.Hwnd);
            if (monitor.IsFallback) continue;
            if (!rulesByMonitor.TryGetValue(monitor.Handle, out var rules))
                rulesByMonitor[monitor.Handle] = rules = MonitorLayoutSettings.Resolve(_settings, monitor).Rules;
            if (!AppRuleService.IsProcessExcluded(rules, raw.ProcessName))
                result.Add(raw.Hwnd);
        }
        _logger?.Info($"Directional focus candidate windows: discovered={rawWindows.Count}, eligible={result.Count}");
        return result;
    }

    private IReadOnlyDictionary<string, Rect> GetDirectionalFocusSlotRects(MonitorInfo monitor)
    {
        var profile = MonitorLayoutSettings.Resolve(_settings, monitor);
        return _layoutEngine.GetSlotRects(profile.Mode, monitor.WorkArea, _settings, profile.Zones);
    }

    private static bool IsDirectionalFocusWindowAvailable(IntPtr hwnd)
        => NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd) &&
           (MonitorNativeMethods.DwmGetWindowAttribute(hwnd, MonitorNativeMethods.DWMWA_CLOAKED,
               out int cloaked, sizeof(int)) != 0 || cloaked == 0);

    private static Rect GetDirectionalFocusWindowRect(IntPtr hwnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out var rect, System.Runtime.InteropServices.Marshal.SizeOf<PeekDows.Core.Win32.RECT>()) == 0 &&
            rect.right > rect.left && rect.bottom > rect.top)
            return new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);

        return NativeMethods.GetWindowRect(hwnd, out rect)
            ? new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top)
            : default;
    }

    private void ExecuteArrange(bool preview = false)
    {
        // Reentrancy guard: ArrangeNow can be triggered by the hotkey, the tray menu and
        // AutoArrange while an earlier arrange is still running (an arrange can block for
        // seconds if a target window is slow). Reject nested entries instead of stacking
        // them on the UI thread.
        if (System.Threading.Interlocked.CompareExchange(ref _arrangeInProgress, 1, 0) != 0)
        {
            _logger.Info("ArrangeNow ignored: arrangement already in progress");
            return;
        }

        try
        {
            _isPreviewOpen = preview;
            ExecuteArrangeCore(preview);
        }
        finally
        {
            _isPreviewOpen = false;
            _arrangeInProgress = 0;
        }
    }

    private void ExecuteArrangeCore(bool preview)
    {
        try
        {
            // Circuit breaker: if Directional Focus just failed against a window (or a window
            // was marked unstable for any reason in the last second), back off rather than
            // immediately re-arranging the same windows. The failure signal means a target is
            // misbehaving; an arrange right now would likely block on the same window again.
            if (_unstableWindowTracker.HasRecentUnstable(withinMs: 1000))
            {
                _logger.Info("ArrangeNow throttled: recent activation failure / unstable window, backing off");
                return;
            }

            _logger.Info("ExecuteArrange started");
            if (!preview) _stackedZones = [];

            _logger.Info("Window discovery refresh started");
            var discovery = preview ? _discoveryService.Fork() : _discoveryService;
            var policy = preview ? _loneWindowMaximizePolicy.Fork() : _loneWindowMaximizePolicy;
            var layout = preview ? new MultiMonitorLayoutService(_monitorService, _layoutEngine, policy) : _multiMonitorLayoutService;
            var diff = discovery.Refresh(_classifier);
            var eligibleWindows = diff.Current;

            _logger.Info($"Window discovery: Added={diff.Added.Count}, Removed={diff.Removed.Count}, Current={diff.Current.Count}");

            if (eligibleWindows.Count == 0 && !preview)
            {
                _logger.Info("No eligible windows found");
                return;
            }

            // P-B5: batch eligible window logs into one IO (was N AppendAllText before async).
            if (eligibleWindows.Count > 0)
            {
                var sb = new System.Text.StringBuilder(eligibleWindows.Count * 96);
                sb.AppendLine($"Eligible windows ({eligibleWindows.Count}):");
                foreach (var w in eligibleWindows)
                    sb.AppendLine($"  hwnd={w.Hwnd}, process={w.ProcessName}, class={w.ClassName}, rect={w.CurrentRect}, fg={w.IsForeground}");
                _logger.Info(sb.ToString().TrimEnd());
            }

            // P-B3 fix: cache MonitorInfo per hwnd for this arrange — avoids 2× GetMonitorForWindow
            // per window (IsNearFullscreen check + NotifyUserRestored + foreground lookup all share it).
            var monitorCache = new Dictionary<IntPtr, MonitorInfo>(eligibleWindows.Count);
            MonitorInfo GetCachedMonitor(IntPtr hwnd)
            {
                if (!monitorCache.TryGetValue(hwnd, out var mi))
                {
                    mi = _monitorService.GetMonitorForWindow(hwnd);
                    monitorCache[hwnd] = mi;
                }
                return mi;
            }

            // LoneWindowMaximizePolicy bookkeeping: prune hwnds that no longer exist, then
            // detect a user restoring a window PeekDows had maximized (respect-user rule).
            // B3 fix: prune against ALL known hwnds (not just eligible) so a PeekDows-
            // maximized window that became ineligible (cloaked/empty title) doesn't stay
            // tracked forever. Also pass handle-aware restore when possible.
            var allKnownHwnds = discovery.GetKnownWindows().Select(w => w.Hwnd).ToList();
            var pruneSet = allKnownHwnds.Count > 0 ? allKnownHwnds : eligibleWindows.Select(w => w.Hwnd).ToList();
            policy.PruneMaximizedSet(pruneSet);
            foreach (var w in eligibleWindows)
            {
                if (policy.WasMaximizedByPeekDows(w.Hwnd) && !w.IsMaximized)
                {
                    var restoredMonitor = GetCachedMonitor(w.Hwnd);
                    // Prefer handle-aware call so policy keys by stable handle.
                    policy.NotifyUserRestored(w.Hwnd, restoredMonitor.Handle, restoredMonitor.WorkArea);
                }
            }

            var foregroundWindow = eligibleWindows.FirstOrDefault(w => w.IsForeground);
            Rect primaryWorkArea;

            if (foregroundWindow != null)
            {
                _logger.Info($"Foreground eligible window found: hwnd={foregroundWindow.Hwnd}");
                var monitorInfo = GetCachedMonitor(foregroundWindow.Hwnd);
                primaryWorkArea = _monitorService.GetWorkArea(monitorInfo);
            }
            else
            {
                _logger.Info("No foreground eligible window; using primary monitor");
                var primaryMonitor = _monitorService.GetPrimaryMonitor();
                primaryWorkArea = _monitorService.GetWorkArea(primaryMonitor);
            }

            _logger.Info($"Primary WorkArea: left={primaryWorkArea.Left}, top={primaryWorkArea.Top}, width={primaryWorkArea.Width}, height={primaryWorkArea.Height}");
            _logger.Info($"Arrange eligibility: allowRepositionMaximized={_settings.AllowRepositionMaximizedWindows}");

            // Eligibility decision is based on the genuine Windows maximized state, NOT on the
            // window rect size/position. A non-maximized window that happens to cover the whole
            // work area (e.g. resized manually to near-fullscreen) is still arrangeable. Only
            // truly maximized windows can be skipped, and only when the setting forbids it.
            var skippedMaximized = new HashSet<IntPtr>();
            var arrangeable = new List<ManagedWindow>();

            foreach (var w in eligibleWindows)
            {
                // A window PeekDows itself maximized must stay arrangeable even when
                // AllowRepositionMaximizedWindows is false — otherwise a lone window that
                // was auto-maximized could never be un-maximized when a second window
                // appears (spec §1, PeekDows-maximized tracking).
                if (!CanRepositionWindow(w.Hwnd, w.IsMaximized, _settings, policy))
                {
                    skippedMaximized.Add(w.Hwnd);
                    _logger.Info($"Window arrange eligibility: hwnd={w.Hwnd}, process={w.ProcessName}, isMaximized=true, allowRepositionMaximized=false, decision=skip maximized");
                }
                else
                {
                    if (w.IsMaximized)
                    {
                        _logger.Info($"Maximized window will be restored and arranged: hwnd={w.Hwnd}, process={w.ProcessName}, allowRepositionMaximized={_settings.AllowRepositionMaximizedWindows}");
                    }
                    else if (IsNearFullscreenWorkArea(w.CurrentRect, _monitorService.GetWorkArea(GetCachedMonitor(w.Hwnd))))
                    {
                        _logger.Info($"Near-fullscreen non-maximized window will be arranged: hwnd={w.Hwnd}, process={w.ProcessName}, rect={w.CurrentRect}");
                    }
                    else
                    {
                        _logger.Info($"Window arrange eligibility: hwnd={w.Hwnd}, process={w.ProcessName}, isMaximized=false, allowRepositionMaximized={_settings.AllowRepositionMaximizedWindows}, decision=arrange");
                    }
                    arrangeable.Add(w);
                }
            }

            _logger.Info($"Maximized windows skipped count={skippedMaximized.Count}");
            _logger.Info($"Arrangeable windows count after maximized filtering={arrangeable.Count}");

            var ratio = LayoutEngine.GetPresetRatio(_settings.WindowSizePreset);
            _logger.Info($"{_settings.LayoutMode} layout selected per monitor: {ratio:P0} overlap ratio");
            var placements = layout.CalculatePlacementsByMonitor(arrangeable, _settings).ToList();
            _logger.Info($"Placement count={placements.Count}");

            if (preview)
            {
                foreach (var placement in placements) GetCachedMonitor(placement.Hwnd);
                var stamp = ReadPreviewStamp();
                var accepted = ShowArrangePreview(placements, eligibleWindows, discovery.GetTopLevelWindows());
                if (!accepted || IsPaused || !_settings.Enabled || placements.Count == 0)
                    return;
                var validatedDiscovery = discovery.Fork();
                if (ReadPreviewStamp() != stamp || !StillMatchesPreview(validatedDiscovery, placements, arrangeable, monitorCache))
                {
                    MessageBox.Show("Windows or screens changed. Open the preview again.", "PeekDows",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                discovery = validatedDiscovery;
            }

            if (placements.Count == 0)
            {
                _logger.Info("No placements generated");
                return;
            }

            // P-B5: batch placements log
            if (placements.Count > 0)
            {
                var sb2 = new System.Text.StringBuilder(placements.Count * 64);
                sb2.AppendLine($"Placements ({placements.Count}):");
                foreach (var p in placements)
                    sb2.AppendLine($"  {p.SlotId}: hwnd={p.Hwnd}, rect={p.TargetRect}, bringToFront={p.BringToFront}");
                _logger.Info(sb2.ToString().TrimEnd());
            }

            // Frame compensation: inflate each target by the window's measured
            // invisible DWM margins so VISIBLE areas tile edge-to-edge (no
            // strips between columns). Zero-margin windows are untouched.
            System.Collections.Generic.IReadOnlyList<WindowPlacement> effectivePlacements = placements;
            try
            {
                effectivePlacements = FrameCompensation.WithCompensatedRects(
                    placements, MeasureFrameMargins(placements));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Frame compensation failed, using uncompensated targets: {ex.Message}");
                effectivePlacements = placements;
            }

            var generation = ++_placementGeneration;
            var customMonitors = effectivePlacements.Where(p => IsCustomSlot(p.SlotId))
                .ToDictionary(p => p.Hwnd, p => GetCachedMonitor(p.Hwnd));
            var targets = effectivePlacements.Select(p => p.Hwnd).ToHashSet();
            var snapshot = _historyService.Capture(arrangeable.Where(w => targets.Contains(w.Hwnd)));
            var result = _placementService.ApplyPlacements(effectivePlacements);
            _historyService.Commit(snapshot, result.ChangedHwnds);

            var acceptedHwnds = result.SucceededHwnds.ToHashSet();
            if (preview && _autoArrangeService.AcknowledgePreviewPlacement(discovery, result))
            {
                _loneWindowMaximizePolicy.Adopt(policy);
            }

            var changedHwnds = result.ChangedHwnds.ToHashSet();
            foreach (var placement in placements)
            {
                if (!changedHwnds.Contains(placement.Hwnd))
                {
                    customMonitors.Remove(placement.Hwnd);
                    continue;
                }

                if (placement.Kind == PlacementKind.Maximize && acceptedHwnds.Contains(placement.Hwnd))
                {
                    _loneWindowMaximizePolicy.NotifyMaximizedByPeekDows(placement.Hwnd);
                }
                else if (placement.Kind == PlacementKind.RestoreAndReposition)
                {
                    _loneWindowMaximizePolicy.NotifyRestoredByPeekDows(placement.Hwnd);
                }
            }

            var succeededPlacements = placements.Where(p => acceptedHwnds.Contains(p.Hwnd)).ToList();
            UpdateStackedZones(succeededPlacements, arrangeable, GetCachedMonitor);
            var topToBottom = new List<IntPtr>(succeededPlacements.Count);
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (acceptedHwnds.Contains(hwnd)) topToBottom.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            _directionalFocusRegistry.UpdateFromPlacements(succeededPlacements, _monitorService,
                topToBottom, GetDirectionalFocusSlotRects);
            _logger.Info($"DirectionalFocusRegistry updated from arrange placements (succeeded={succeededPlacements.Count} of {placements.Count})");

            if (result.FailedCount > 0)
            {
                _logger.Warn($"Arrange completed with failures: attempted={result.AttemptedCount}, succeeded={result.SucceededCount}, failed={result.FailedCount}");
                foreach (var error in result.Errors)
                {
                    _logger.Warn($"Arrange error: {error}");
                }
            }
            else
            {
                _logger.Info($"Arrange completed successfully: {result.SucceededCount} window(s) placed");
            }

            // Post-arrange verification: re-read actual rects and name any window
            // that is not where it was told to go (app clamped its min size, ...).
            // Compared against the COMPENSATED targets (what was actually requested).
            // Maximized-origin windows are skipped (async restore still settling).
            // With animation on, tweens settle ~150ms after apply: verify once
            // settled instead of measuring mid-flight positions.
            if (_settings.AnimateWindowTransitions || effectivePlacements.Any(p => IsCustomSlot(p.SlotId)))
            {
                var verifyTargets = effectivePlacements.Where(p => !IsCustomSlot(p.SlotId) ||
                    acceptedHwnds.Contains(p.Hwnd)).ToArray();
                var verifyArrangeable = arrangeable;
                System.Windows.Forms.Timer? verifyTimer = null;
                verifyTimer = new System.Windows.Forms.Timer { Interval = 400 };
                verifyTimer.Tick += (s, e) =>
                {
                    try
                    {
                        verifyTimer?.Stop();
                        verifyTimer?.Dispose();
                    }
                    catch { }
                    if (generation != _placementGeneration || IsPaused || !_settings.Enabled)
                        return;
                    RunPlacementVerification(verifyTargets, verifyArrangeable, customMonitors);
                };
                verifyTimer.Start();
            }
            else
            {
                RunPlacementVerification(effectivePlacements, arrangeable, customMonitors);
            }

            if (result.SucceededHwnds.Count > 0 && _settings.ArrangeSoundEnabled)
            {
                try { _soundService.Play(_settings.ArrangeSoundName); }
                catch (Exception ex) { _logger.Warn($"ArrangeSound trigger failed: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("ExecuteArrange failed", ex);
        }
    }

    /// <summary>
    /// Returns true when a non-maximized window's rect covers the whole work area. Used only to
    /// emit an informational log explaining that such a window is still being arranged (the
    /// historic bug was to skip it). It never drives the skip decision.
    /// </summary>
    private static bool IsNearFullscreenWorkArea(Core.Models.Rect windowRect, Core.Models.Rect workArea)
    {
        return windowRect.Left <= workArea.Left
            && windowRect.Top <= workArea.Top
            && windowRect.Right >= workArea.Right
            && windowRect.Bottom >= workArea.Bottom;
    }

    /// <summary>
    /// Measures each window's invisible DWM frame margins (outer GetWindowRect
    /// minus visible EXTENDED_FRAME_BOUNDS, per side, clamped to zero).
    /// Failures yield zero margins (current behavior). Never throws.
    /// </summary>
    private System.Collections.Generic.Dictionary<IntPtr, FrameMargins> MeasureFrameMargins(
        System.Collections.Generic.IReadOnlyList<WindowPlacement> placements)
    {
        var result = new System.Collections.Generic.Dictionary<IntPtr, FrameMargins>(placements.Count);
        foreach (var p in placements)
        {
            try
            {
                if (!NativeMethods.GetWindowRect(p.Hwnd, out var outer))
                    continue;
                if (NativeMethods.DwmGetWindowAttribute(
                        p.Hwnd,
                        NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                        out var visible,
                        System.Runtime.InteropServices.Marshal.SizeOf<PeekDows.Core.Win32.RECT>()) != 0)
                    continue;
                result[p.Hwnd] = FrameCompensation.Measure(
                    new Rect(outer.left, outer.top, outer.right - outer.left, outer.bottom - outer.top),
                    new Rect(visible.left, visible.top, visible.right - visible.left, visible.bottom - visible.top));
            }
            catch
            {
                // Unmeasurable window keeps zero margins.
            }
        }
        return result;
    }

    /// <summary>
    /// Compares requested targets with live rects and logs deviations.
    /// Called synchronously (no animation) or ~400ms after apply (animated,
    /// tweens settled). Never throws.
    /// </summary>
    private void RunPlacementVerification(
        System.Collections.Generic.IReadOnlyList<WindowPlacement> targets,
        System.Collections.Generic.IReadOnlyList<ManagedWindow> arrangeable,
        IReadOnlyDictionary<IntPtr, MonitorInfo> customMonitors)
    {
        try
        {
            var customWindows = targets.Where(p => IsCustomSlot(p.SlotId)).Select(p => p.Hwnd).ToHashSet();
            var maximized = new System.Collections.Generic.HashSet<IntPtr>();
            foreach (var w in arrangeable)
                if (w.IsMaximized && (!customWindows.Contains(w.Hwnd) || NativeMethods.IsZoomed(w.Hwnd)))
                    maximized.Add(w.Hwnd);
            System.Func<IntPtr, Rect?> readRect = hwnd =>
            {
                try
                {
                    if (!NativeMethods.GetWindowRect(hwnd, out var nr)) return null;
                    return new Rect(nr.left, nr.top, nr.right - nr.left, nr.bottom - nr.top);
                }
                catch { return null; }
            };
            var deviations = PlacementVerifier.FindDeviations(targets, readRect, maximized.Contains);
            ApplyCustomZoneFallbacks(arrangeable, deviations, customMonitors);
            if (deviations.Count == 0)
                _logger.Info($"Placement verification clean: {targets.Count} window(s) on target");
            else
                foreach (var d in deviations)
                    _logger.Warn($"Placement deviation: hwnd={d.Hwnd}, expected={d.Expected}, actual={d.Actual}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Placement verification failed: {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _animationService.SnapAllToTarget(); } catch { }
            try
            {
                _directionalFocusService.GestureStarted -= OnFocusHintGestureStarted;
                _directionalFocusService.GestureUpdated -= OnFocusHintGestureUpdated;
                _directionalFocusService.GestureEnded -= OnFocusHintGestureEnded;
            }
            catch { }
            HideFocusHints();
            try { _hintOverlay?.Dispose(); } catch { }
            try { _iconService.Dispose(); } catch { }
            try { _kbWatchService.Dispose(); } catch { }
            _pauseCheckTimer.Stop();
            _pauseCheckTimer.Dispose();
            try { _soundAutoOffTimer.Stop(); } catch { }
            try { _soundAutoOffTimer.Dispose(); } catch { }
            try { _soundService.Dispose(); } catch { }
            _autoArrangeService.Dispose();
            _directionalFocusService.Dispose();
            _animationService.Dispose();
            _hotkeyService.Dispose();
            _trayController.Dispose();
        }
        base.Dispose(disposing);
    }
}
