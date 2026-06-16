using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using PeekDows.App.Hotkeys;
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

    private RuntimeState _state = RuntimeState.Running;
    private AppSettings _settings;

    public RuntimeState State => _state;

    public event Action<RuntimeState>? StateChanged;

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
        _placementService = new WindowPlacementService(_logger);

        _logger.Info("Hotkey registration started");
        _hotkeyService = new HotkeyService(_logger);
        _hotkeyService.ArrangeNowRequested += OnArrangeNowRequested;

        if (!_hotkeyService.RegisterArrangeHotkey())
        {
            _logger.Warn("Failed to register Ctrl+Alt+Space hotkey. It may already be in use.");
        }

        _trayController = new TrayIconController(this, _logger);
        _logger.Info("Tray initialized");
        _logger.Info("PeekDows ready");
    }

    public void ArrangeNow()
    {
        _logger.Info("ArrangeNow requested");

        if (_state == RuntimeState.Paused)
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
        _state = _state == RuntimeState.Running ? RuntimeState.Paused : RuntimeState.Running;
        _logger.Info($"State toggled to {_state}");
        StateChanged?.Invoke(_state);
    }

    public void OpenSettings()
    {
        _trayController.OpenSettings();
    }

    public void Exit()
    {
        _logger.Info("PeekDows exiting");
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
            Rect workArea;

            if (foregroundWindow != null)
            {
                _logger.Info($"Foreground eligible window found: hwnd={foregroundWindow.Hwnd}");
                var monitorInfo = _monitorService.GetMonitorForWindow(foregroundWindow.Hwnd);
                workArea = _monitorService.GetWorkArea(monitorInfo);
                _logger.Info($"Using monitor for foreground window");
            }
            else
            {
                _logger.Info("No foreground eligible window; using primary monitor");
                var primaryMonitor = _monitorService.GetPrimaryMonitor();
                workArea = _monitorService.GetWorkArea(primaryMonitor);
            }

            _logger.Info($"WorkArea: left={workArea.Left}, top={workArea.Top}, width={workArea.Width}, height={workArea.Height}");

            _logger.Info("Fullscreen check started");
            var fullscreenHwnds = eligibleWindows
                .Where(w => _classifier.IsConsideredFullscreen(w.IsMaximized, w.IsVisible, w.IsMinimized, w.CurrentRect, workArea))
                .Select(w => w.Hwnd)
                .ToHashSet();

            foreach (var fsw in eligibleWindows.Where(w => fullscreenHwnds.Contains(w.Hwnd)))
            {
                _logger.Info($"Skipped fullscreen non-maximized window: hwnd={fsw.Hwnd}, title={fsw.Title}, rect={fsw.CurrentRect}");
            }

            foreach (var mw in eligibleWindows.Where(w => w.IsMaximized && IsRectFullscreen(w.CurrentRect, workArea)))
            {
                _logger.Info($"Maximized window will be restored and arranged: hwnd={mw.Hwnd}, title={mw.Title}");
            }

            _logger.Info($"Fullscreen windows skipped count={fullscreenHwnds.Count}");

            var arrangeable = eligibleWindows.Where(w => !fullscreenHwnds.Contains(w.Hwnd)).ToList();
            _logger.Info($"Arrangeable windows count after fullscreen filtering={arrangeable.Count}");

            if (arrangeable.Count == 0)
            {
                _logger.Info("No arrangeable windows after fullscreen filtering");
                return;
            }

            _logger.Info("ClassicPeekGrid layout selected");
            var placements = _layoutEngine.CalculateClassicPeekGridPlacements(arrangeable, workArea, _settings);
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
            _hotkeyService.Dispose();
            _trayController.Dispose();
        }
        base.Dispose(disposing);
    }
}
