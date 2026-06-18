using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PeekDows.App.Settings;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Tray;

public class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuBuilder _menuBuilder;
    private readonly IPeekDowsController _controller;
    private readonly FileLogger? _logger;
    private readonly Icon _trayIcon;
    private readonly bool _ownsTrayIcon;
    private readonly SettingsService? _settingsService;
    private SettingsWindow? _settingsWindow;

    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _pauseResumeItem;
    private ToolStripMenuItem? _autoArrangeItem;
    private ToolStripMenuItem? _startWithWindowsItem;

    public TrayIconController(IPeekDowsController controller) : this(controller, null) { }

    public TrayIconController(IPeekDowsController controller, FileLogger? logger) : this(controller, logger, null) { }

    public TrayIconController(IPeekDowsController controller, FileLogger? logger, SettingsService? settingsService)
    {
        _controller = controller;
        _logger = logger;
        _settingsService = settingsService;
        _menuBuilder = new TrayMenuBuilder(controller, this);

        _trayIcon = LoadTrayIcon();
        _ownsTrayIcon = _trayIcon != SystemIcons.Application;

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "PeekDows",
            ContextMenuStrip = _menuBuilder.Build(),
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => _controller.OpenSettings();

        _controller.StateChanged += OnStateChanged;
        _controller.AutoArrangeChanged += OnAutoArrangeChanged;
        _controller.StartWithWindowsChanged += OnStartWithWindowsChanged;

        UpdateMenuState(_controller.State);
        UpdateAutoArrangeMenu(_controller.CurrentSettings?.AutoArrange ?? false);
        UpdateStartWithWindowsMenu(_controller.CurrentSettings?.StartWithWindows ?? false);
    }

    internal void InitializeMenuReferences(ToolStripMenuItem statusItem, ToolStripMenuItem pauseResumeItem, ToolStripMenuItem autoArrangeItem, ToolStripMenuItem startWithWindowsItem)
    {
        _statusItem = statusItem;
        _pauseResumeItem = pauseResumeItem;
        _autoArrangeItem = autoArrangeItem;
        _startWithWindowsItem = startWithWindowsItem;
        UpdateMenuState(_controller.State);
        UpdateAutoArrangeMenu(_controller.CurrentSettings?.AutoArrange ?? false);
        UpdateStartWithWindowsMenu(_controller.CurrentSettings?.StartWithWindows ?? false);
    }

    public void OpenSettings()
    {
        if (_settingsWindow == null || _settingsWindow.IsDisposed)
        {
            var settings = _controller.CurrentSettings;
            var settingsWindow = _settingsService != null
                ? new SettingsWindow(settings, _settingsService)
                : new SettingsWindow(settings, new SettingsService());
            settingsWindow.SettingsSaved += () => _controller.OnSettingsChanged();
            _settingsWindow = settingsWindow;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == FormWindowState.Minimized)
            {
                _settingsWindow.WindowState = FormWindowState.Normal;
            }
            _settingsWindow.BringToFront();
        }
    }

    private void OnStateChanged(RuntimeState state)
    {
        UpdateMenuState(state);
        _notifyIcon.Text = state == RuntimeState.Paused ? "PeekDows (Paused)" : "PeekDows";
    }

    private Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "peekdows_tray_icon_bw.ico");

        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        _logger?.Warn($"Tray icon file not found: {iconPath}. Falling back to default icon.");
        return SystemIcons.Application;
    }

    private void OnAutoArrangeChanged(bool isAutoArrange)
    {
        UpdateAutoArrangeMenu(isAutoArrange);
    }

    private void OnStartWithWindowsChanged(bool isEnabled)
    {
        UpdateStartWithWindowsMenu(isEnabled);
    }

    private void UpdateMenuState(RuntimeState state)
    {
        if (_statusItem != null)
        {
            _statusItem.Text = state == RuntimeState.Paused ? "Status: Paused" : "Status: Running";
        }

        if (_pauseResumeItem != null)
        {
            _pauseResumeItem.Text = state == RuntimeState.Paused ? "Resume" : "Pause";
        }
    }

    private void UpdateAutoArrangeMenu(bool isAutoArrange)
    {
        if (_autoArrangeItem != null)
        {
            _autoArrangeItem.Text = isAutoArrange ? "Disable Auto Arrange" : "Enable Auto Arrange";
        }
    }

    private void UpdateStartWithWindowsMenu(bool isEnabled)
    {
        if (_startWithWindowsItem != null)
        {
            _startWithWindowsItem.Checked = isEnabled;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_ownsTrayIcon)
        {
            _trayIcon.Dispose();
        }
        _settingsWindow?.Dispose();
    }
}
