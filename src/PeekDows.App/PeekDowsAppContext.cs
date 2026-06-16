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

    private RuntimeState _state = RuntimeState.Running;
    private AppSettings _settings;

    public RuntimeState State => _state;

    public event Action<RuntimeState>? StateChanged;

    public PeekDowsAppContext()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        _discoveryService = new WindowDiscoveryService();
        _classifier = new WindowClassifier(_settings);
        _monitorService = new MonitorService();
        _layoutEngine = new LayoutEngine();
        _placementService = new WindowPlacementService();

        _hotkeyService = new HotkeyService();
        _hotkeyService.ArrangeNowRequested += OnArrangeNowRequested;

        if (!_hotkeyService.RegisterArrangeHotkey())
        {
            Debug.WriteLine("PeekDows: failed to register Ctrl+Alt+Space hotkey. It may already be in use.");
        }

        _trayController = new TrayIconController(this);
    }

    public void ArrangeNow()
    {
        if (_state == RuntimeState.Paused) return;
        if (!_settings.Enabled) return;

        ExecuteArrange();
    }

    public void TogglePause()
    {
        _state = _state == RuntimeState.Running ? RuntimeState.Paused : RuntimeState.Running;
        StateChanged?.Invoke(_state);
    }

    public void OpenSettings()
    {
        _trayController.OpenSettings();
    }

    public void Exit()
    {
        _hotkeyService.Dispose();
        Application.Exit();
    }

    private void OnArrangeNowRequested()
    {
        ArrangeNow();
    }

    private void ExecuteArrange()
    {
        try
        {
            var diff = _discoveryService.Refresh(_classifier);
            var eligibleWindows = diff.Current;

            if (eligibleWindows.Count == 0) return;

            var foregroundWindow = eligibleWindows.FirstOrDefault(w => w.IsForeground);
            Rect workArea;

            if (foregroundWindow != null)
            {
                var monitorInfo = _monitorService.GetMonitorForWindow(foregroundWindow.Hwnd);
                workArea = _monitorService.GetWorkArea(monitorInfo);
            }
            else
            {
                var primaryMonitor = _monitorService.GetPrimaryMonitor();
                workArea = _monitorService.GetWorkArea(primaryMonitor);
            }

            var fullscreenHwnds = eligibleWindows
                .Where(w => _classifier.IsFullscreen(new RawWindowInfo
                {
                    Hwnd = w.Hwnd,
                    CurrentRect = w.CurrentRect,
                    IsVisible = w.IsVisible,
                    IsMinimized = w.IsMinimized
                }, workArea))
                .Select(w => w.Hwnd)
                .ToHashSet();

            var arrangeable = eligibleWindows.Where(w => !fullscreenHwnds.Contains(w.Hwnd)).ToList();

            if (arrangeable.Count == 0) return;

            var placements = _layoutEngine.CalculatePlacements(arrangeable, workArea, _settings);

            var result = _placementService.ApplyPlacements(placements);

            if (result.FailedCount > 0)
            {
                Debug.WriteLine($"PeekDows: Arrange completed with failures. Attempted={result.AttemptedCount}, Succeeded={result.SucceededCount}, Failed={result.FailedCount}");
                foreach (var error in result.Errors)
                {
                    Debug.WriteLine($"PeekDows: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PeekDows: ArrangeNow failed: {ex}");
        }
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
