using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using PeekDows.App.AutoArrange;
using PeekDows.App.Hotkeys;
using PeekDows.App.Startup;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App;

public class PeekDowsAppContext : ApplicationContext, IPeekDowsController
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

    private AppSettings _settings;

    public RuntimeState State => _pauseState.State;
    public bool IsPaused => _pauseState.IsPaused;
    public DateTimeOffset? PauseUntil => _pauseState.PauseUntil;
    public string? PauseDescription => _pauseState.GetPauseDescription();

    public AppSettings CurrentSettings => _settings;

    public bool IsAutoArrangeRunning => _autoArrangeService.IsRunning;

    public bool IsStartWithWindowsEnabled => _settings.StartWithWindows;

    public event Action<RuntimeState>? StateChanged;
    public event Action<bool>? AutoArrangeChanged;
    public event Action<bool>? StartWithWindowsChanged;

    public string LogFilePath => _logger.LogFilePath;

    public PeekDowsAppContext()
    {
        _logger = new FileLogger();
        _logger.Info("PeekDows starting");

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _logger.Info($"Settings loaded: path={_settingsService.GetType().GetProperty("SettingsFilePath")?.GetValue(_settingsService) ?? "N/A"}");
        _logger.Info($"Settings: Enabled={_settings.Enabled}");
        _logger.Info($"Settings: AutoArrange={_settings.AutoArrange}");
        _logger.Info($"Settings: SingleWindowMode={_settings.SingleWindowMode}");
        _logger.Info($"Settings: IgnoredProcesses.Count={_settings.IgnoredProcesses.Count}");
        _logger.Info($"Settings: IgnoredClasses.Count={_settings.IgnoredClasses.Count}");

        _discoveryService = new WindowDiscoveryService();
        _classifier = new WindowClassifier(_settings);
        _monitorService = new MonitorService(_logger);
        _layoutEngine = new LayoutEngine();
        _multiMonitorLayoutService = new MultiMonitorLayoutService(_monitorService, _layoutEngine, _logger);
        _placementService = new WindowPlacementService(_logger);

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
        _pauseState.TogglePause();
    }

    public void PauseUntilResumed()
    {
        _logger.Info("Pause enabled until resumed");
        _pauseState.PauseUntilResumed();
        StartPauseCheckTimerIfNeeded();
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
            _pauseCheckTimer.Stop();
            _logger.Info("Pause expired, resuming");
        }

        if (!_pauseState.IsPaused)
        {
            _pauseCheckTimer.Stop();
        }
    }

    private void OnPauseStateChanged(RuntimeState state)
    {
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
        _pauseCheckTimer.Stop();
        _pauseCheckTimer.Dispose();
        _autoArrangeService.Dispose();
        _hotkeyService.Dispose();
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
        _logger.Info($"Settings changed at runtime: IgnoredProcesses.Count={_settings.IgnoredProcesses.Count}, IgnoredClasses.Count={_settings.IgnoredClasses.Count}");
    }

    private void OnArrangeNowRequested()
    {
        ArrangeNow();
    }

    private void ExecuteArrange()
    {
        try
        {
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

            foreach (var w in eligibleWindows)
            {
                _logger.Info($"Eligible window: hwnd={w.Hwnd}, title={w.Title}, process={w.ProcessName}, class={w.ClassName}, rect={w.CurrentRect}, foreground={w.IsForeground}");
            }

            var foregroundWindow = eligibleWindows.FirstOrDefault(w => w.IsForeground);
            Rect primaryWorkArea;

            if (foregroundWindow != null)
            {
                _logger.Info($"Foreground eligible window found: hwnd={foregroundWindow.Hwnd}");
                var monitorInfo = _monitorService.GetMonitorForWindow(foregroundWindow.Hwnd);
                primaryWorkArea = _monitorService.GetWorkArea(monitorInfo);
            }
            else
            {
                _logger.Info("No foreground eligible window; using primary monitor");
                var primaryMonitor = _monitorService.GetPrimaryMonitor();
                primaryWorkArea = _monitorService.GetWorkArea(primaryMonitor);
            }

            _logger.Info($"Primary WorkArea (for fullscreen check): left={primaryWorkArea.Left}, top={primaryWorkArea.Top}, width={primaryWorkArea.Width}, height={primaryWorkArea.Height}");

            _logger.Info("Fullscreen check started");
            var fullscreenHwnds = new HashSet<IntPtr>();

            foreach (var w in eligibleWindows)
            {
                var windowMonitor = _monitorService.GetMonitorForWindow(w.Hwnd);
                var windowWorkArea = _monitorService.GetWorkArea(windowMonitor);

                if (_classifier.IsConsideredFullscreen(w.IsMaximized, w.IsVisible, w.IsMinimized, w.CurrentRect, windowWorkArea))
                {
                    fullscreenHwnds.Add(w.Hwnd);
                    _logger.Info($"Skipped fullscreen non-maximized window: hwnd={w.Hwnd}, title={w.Title}, rect={w.CurrentRect}");
                }
            }

            foreach (var mw in eligibleWindows.Where(w => w.IsMaximized && !fullscreenHwnds.Contains(w.Hwnd)))
            {
                var windowMonitor = _monitorService.GetMonitorForWindow(mw.Hwnd);
                var windowWorkArea = _monitorService.GetWorkArea(windowMonitor);
                if (IsRectFullscreen(mw.CurrentRect, windowWorkArea))
                {
                    _logger.Info($"Maximized window will be restored and arranged: hwnd={mw.Hwnd}, title={mw.Title}");
                }
            }

            _logger.Info($"Fullscreen windows skipped count={fullscreenHwnds.Count}");

            var arrangeable = eligibleWindows.Where(w => !fullscreenHwnds.Contains(w.Hwnd)).ToList();
            _logger.Info($"Arrangeable windows count after fullscreen filtering={arrangeable.Count}");

            if (arrangeable.Count == 0)
            {
                _logger.Info("No arrangeable windows after fullscreen filtering");
                return;
            }

            _logger.Info("ClassicPeekGrid layout selected per monitor: 90% overlap ratio");
            var placements = _multiMonitorLayoutService.CalculatePlacementsByMonitor(arrangeable, _settings);
            _logger.Info($"Placement count={placements.Count}");

            if (placements.Count == 0)
            {
                _logger.Info("No placements generated");
                return;
            }

            foreach (var p in placements)
            {
                var window = arrangeable.FirstOrDefault(w => w.Hwnd == p.Hwnd);
                _logger.Info($"Placement {p.SlotId}: hwnd={p.Hwnd}, title={window?.Title ?? "unknown"}, rect={p.TargetRect}, bringToFront={p.BringToFront}");
            }

            var result = _placementService.ApplyPlacements(placements);

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

    private static bool IsRectFullscreen(Core.Models.Rect windowRect, Core.Models.Rect workArea)
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
            _pauseCheckTimer.Stop();
            _pauseCheckTimer.Dispose();
            _autoArrangeService.Dispose();
            _hotkeyService.Dispose();
            _trayController.Dispose();
        }
        base.Dispose(disposing);
    }
}
