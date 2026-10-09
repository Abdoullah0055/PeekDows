using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using PeekDows.App.AutoArrange;
using PeekDows.App.Animation;
using PeekDows.App.Diagnostics;
using PeekDows.App.Focus;
using PeekDows.App.Hotkeys;
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
    private readonly IVirtualDesktopService _virtualDesktopService;
    private readonly WindowActivationService _windowActivationService;
    private readonly UnstableWindowTracker _unstableWindowTracker;
    private readonly WindowAnimationService _animationService;
    private readonly LoneWindowMaximizePolicy _loneWindowMaximizePolicy;
    private int _arrangeInProgress;

    private AppSettings _settings;

    // Focus hint overlay (Beta): created lazily on the UI thread, driven by
    // DirectionalFocusService gesture events. Never focusable/clickable.
    private HintOverlayForm? _hintOverlay;
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

        if (!_hotkeyService.RegisterArrangeHotkey())
        {
            _logger.Warn("Failed to register Ctrl+Alt+Space hotkey. It may already be in use.");
        }

        if (!_hotkeyService.RegisterPauseHotkey())
        {
            _logger.Warn("Failed to register Ctrl+Alt+P hotkey. It may already be in use.");
        }

        _trayController = new TrayIconController(this, _logger, _settingsService);
        _logger.Info("Tray initialized");

        _startupService = new StartupService(_logger);
        SyncStartWithWindows();

        _autoArrangeService = new AutoArrangeService(
            _discoveryService, _classifier, this, _logger);

        if (_settings.AutoArrange)
        {
            _autoArrangeService.Start(_settings);
        }

        _directionalFocusRegistry = new DirectionalFocusRegistry(
            hwnd => NativeMethods.IsWindow(hwnd),
            hwnd => _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd),
            _logger);
        var gestureDetector = new DirectionalFocusGestureDetector();
        _windowActivationService = new WindowActivationService(new Win32ActivationApi(new Win32InputSimulator(_logger)), _logger, _unstableWindowTracker);

        _directionalFocusSnapshotService = new DirectionalFocusLayoutSnapshotService(
            _monitorService,
            _layoutEngine,
            hwnd =>
            {
                if (NativeMethods.GetWindowRect(hwnd, out var nativeRect))
                {
                    return new Rect(nativeRect.left, nativeRect.top, nativeRect.right - nativeRect.left, nativeRect.bottom - nativeRect.top);
                }
                return default;
            },
            hwnd => _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd),
            () => LayoutEngine.GetPresetRatio(_settings.WindowSizePreset),
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

        if (_settings.DirectionalFocusEnabled)
        {
            _directionalFocusService.Start();
        }

    // Layout/flip + Taskmgr tracer: always active, even when Directional
    // Focus is off, so layout flips stay explainable in all configurations.
    // Polling-only design: no keyboard hook is installed anymore.
    _kbWatchService = new ForegroundWatchService(_logger);
        _kbWatchService.Start();

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
        _logger.Info($"AllowRepositionMaximizedWindows changed: {_settings.AllowRepositionMaximizedWindows}");

        AllowRepositionMaximizedWindowsChanged?.Invoke(_settings.AllowRepositionMaximizedWindows);
    }

    public void SetWindowSizePreset(WindowSizePreset preset)
    {
        if (_settings.WindowSizePreset == preset) return;

        _settings.WindowSizePreset = preset;
        _settingsService.Save(_settings);
        _logger.Info($"WindowSizePreset changed: {preset} = {LayoutEngine.GetPresetRatio(preset):P0}");
        WindowSizePresetChanged?.Invoke(preset);
        ArrangeNow();
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
            try { _kbWatchService.Dispose(); } catch { }
        _pauseCheckTimer.Stop();
        _pauseCheckTimer.Dispose();
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
            var populated = new System.Collections.Generic.List<DirectionalFocusSlot>(available.Count);
            foreach (var (s, _) in available)
                populated.Add(s);

            bool hasTarget = slot != null && populated.Contains(slot.Value);
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

    /// <summary>
    /// Returns the hwnds of currently eligible windows (visible, on the current
    /// virtual desktop, not a system/shell window). Used by Directional Focus to
    /// rebuild the slot map on demand without moving anything.
    /// </summary>
    private IReadOnlyList<IntPtr> GetDirectionalFocusCandidateWindows()
    {
        var rawWindows = _discoveryService.GetTopLevelWindows();
        var result = new List<IntPtr>(rawWindows.Count);
        foreach (var raw in rawWindows)
        {
            if (_classifier.IsEligible(raw))
            {
                result.Add(raw.Hwnd);
            }
        }
        _logger?.Info($"Directional focus candidate windows: discovered={rawWindows.Count}, eligible={result.Count}");
        return result;
    }

    private void ExecuteArrange()
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
            ExecuteArrangeCore();
        }
        finally
        {
            _arrangeInProgress = 0;
        }
    }

    private void ExecuteArrangeCore()
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

            _logger.Info("Window discovery refresh started");
            var diff = _discoveryService.Refresh(_classifier);
            var eligibleWindows = diff.Current;

            _logger.Info($"Window discovery: Added={diff.Added.Count}, Removed={diff.Removed.Count}, Current={diff.Current.Count}");

            if (eligibleWindows.Count == 0)
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
                    sb.AppendLine($"  hwnd={w.Hwnd}, title={w.Title}, process={w.ProcessName}, class={w.ClassName}, rect={w.CurrentRect}, fg={w.IsForeground}");
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
            var allKnownHwnds = _discoveryService.GetKnownWindows().Select(w => w.Hwnd).ToList();
            var pruneSet = allKnownHwnds.Count > 0 ? allKnownHwnds : eligibleWindows.Select(w => w.Hwnd).ToList();
            _loneWindowMaximizePolicy.PruneMaximizedSet(pruneSet);
            foreach (var w in eligibleWindows)
            {
                if (_loneWindowMaximizePolicy.WasMaximizedByPeekDows(w.Hwnd) && !w.IsMaximized)
                {
                    var restoredMonitor = GetCachedMonitor(w.Hwnd);
                    // Prefer handle-aware call so policy keys by stable handle.
                    _loneWindowMaximizePolicy.NotifyUserRestored(w.Hwnd, restoredMonitor.Handle, restoredMonitor.WorkArea);
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
                bool maximizedByPeekDows = _loneWindowMaximizePolicy.WasMaximizedByPeekDows(w.Hwnd);
                if (w.IsMaximized && !_settings.AllowRepositionMaximizedWindows && !maximizedByPeekDows)
                {
                    skippedMaximized.Add(w.Hwnd);
                    _logger.Info($"Window arrange eligibility: hwnd={w.Hwnd}, title={w.Title}, isMaximized=true, allowRepositionMaximized=false, decision=skip maximized");
                }
                else
                {
                    if (w.IsMaximized)
                    {
                        _logger.Info($"Window arrange eligibility: hwnd={w.Hwnd}, title={w.Title}, isMaximized=true, allowRepositionMaximized=true, decision=arrange");
                        _logger.Info($"Maximized window will be restored and arranged: hwnd={w.Hwnd}, title={w.Title}");
                    }
                    else if (IsNearFullscreenWorkArea(w.CurrentRect, _monitorService.GetWorkArea(GetCachedMonitor(w.Hwnd))))
                    {
                        _logger.Info($"Near-fullscreen non-maximized window will be arranged: hwnd={w.Hwnd}, title={w.Title}, rect={w.CurrentRect}");
                    }
                    else
                    {
                        _logger.Info($"Window arrange eligibility: hwnd={w.Hwnd}, title={w.Title}, isMaximized=false, allowRepositionMaximized={_settings.AllowRepositionMaximizedWindows}, decision=arrange");
                    }
                    arrangeable.Add(w);
                }
            }

            _logger.Info($"Maximized windows skipped count={skippedMaximized.Count}");
            _logger.Info($"Arrangeable windows count after maximized filtering={arrangeable.Count}");

            if (arrangeable.Count == 0)
            {
                _logger.Info("No arrangeable windows after maximized filtering");
                return;
            }

            var ratio = LayoutEngine.GetPresetRatio(_settings.WindowSizePreset);
            _logger.Info($"ClassicPeekGrid layout selected per monitor: {ratio:P0} overlap ratio");
            var placements = _multiMonitorLayoutService.CalculatePlacementsByMonitor(arrangeable, _settings);
            _logger.Info($"Placement count={placements.Count}");

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
                // avoid LINQ FirstOrDefault per placement — build hwnd→title map once
                var titleMap = new System.Collections.Generic.Dictionary<IntPtr, string>(arrangeable.Count);
                foreach (var w in arrangeable) titleMap[w.Hwnd] = w.Title;
                foreach (var p in placements)
                {
                    titleMap.TryGetValue(p.Hwnd, out var t);
                    sb2.AppendLine($"  {p.SlotId}: hwnd={p.Hwnd}, title={t ?? "unknown"}, rect={p.TargetRect}, bringToFront={p.BringToFront}");
                }
                _logger.Info(sb2.ToString().TrimEnd());
            }

            var result = _placementService.ApplyPlacements(placements);

            foreach (var p in result.SucceededHwnds)
            {
                var kind = placements.First(pl => pl.Hwnd == p).Kind;
                if (kind == PlacementKind.Maximize)
                {
                    _loneWindowMaximizePolicy.NotifyMaximizedByPeekDows(p);
                }
                else if (kind == PlacementKind.RestoreAndReposition)
                {
                    _loneWindowMaximizePolicy.NotifyRestoredByPeekDows(p);
                }
            }

            var succeededPlacements = placements.Where(p => result.SucceededHwnds.Contains(p.Hwnd)).ToList();
            _directionalFocusRegistry.UpdateFromPlacements(succeededPlacements, _monitorService);
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
            try { _kbWatchService.Dispose(); } catch { }
            _pauseCheckTimer.Stop();
            _pauseCheckTimer.Dispose();
            _autoArrangeService.Dispose();
            _directionalFocusService.Dispose();
            _animationService.Dispose();
            _hotkeyService.Dispose();
            _trayController.Dispose();
        }
        base.Dispose(disposing);
    }
}
