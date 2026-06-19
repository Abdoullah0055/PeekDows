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
    private ToolStripMenuItem? _repositionMaximizedItem;
    private ToolStripMenuItem? _startWithWindowsItem;
    private ToolStripMenuItem? _directionalFocusItem;

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
        _controller.DirectionalFocusChanged += OnDirectionalFocusChanged;
        _controller.AllowRepositionMaximizedWindowsChanged += OnAllowRepositionMaximizedWindowsChanged;

        UpdateMenuState(_controller.State);
        UpdateAutoArrangeMenu(_controller.CurrentSettings?.AutoArrange ?? false);
        UpdateStartWithWindowsMenu(_controller.CurrentSettings?.StartWithWindows ?? false);
        UpdateDirectionalFocusMenu(_controller.CurrentSettings?.DirectionalFocusEnabled ?? false);
        UpdateRepositionMaximizedMenu(_controller.CurrentSettings?.AllowRepositionMaximizedWindows ?? false);
    }

    internal void InitializeMenuReferences(ToolStripMenuItem statusItem, ToolStripMenuItem pauseResumeItem, ToolStripMenuItem autoArrangeItem, ToolStripMenuItem repositionMaximizedItem, ToolStripMenuItem startWithWindowsItem, ToolStripMenuItem directionalFocusItem)
    {
        _statusItem = statusItem;
        _pauseResumeItem = pauseResumeItem;
        _autoArrangeItem = autoArrangeItem;
        _repositionMaximizedItem = repositionMaximizedItem;
        _startWithWindowsItem = startWithWindowsItem;
        _directionalFocusItem = directionalFocusItem;
        UpdateMenuState(_controller.State);
        UpdateAutoArrangeMenu(_controller.CurrentSettings?.AutoArrange ?? false);
        UpdateStartWithWindowsMenu(_controller.CurrentSettings?.StartWithWindows ?? false);
        UpdateDirectionalFocusMenu(_controller.CurrentSettings?.DirectionalFocusEnabled ?? false);
        UpdateRepositionMaximizedMenu(_controller.CurrentSettings?.AllowRepositionMaximizedWindows ?? false);
    }

    internal void UpdatePauseState()
    {
        UpdateMenuState(_controller.State);
        _notifyIcon.Text = _controller.State == RuntimeState.Paused
            ? (_controller.PauseDescription ?? "PeekDows (Paused)")
            : "PeekDows";
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
        _notifyIcon.Text = state == RuntimeState.Paused
            ? (_controller.PauseDescription ?? "PeekDows (Paused)")
            : "PeekDows";
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

    private void OnDirectionalFocusChanged(bool isEnabled)
    {
        UpdateDirectionalFocusMenu(isEnabled);
    }

    private void OnAllowRepositionMaximizedWindowsChanged(bool isEnabled)
    {
        UpdateRepositionMaximizedMenu(isEnabled);
    }

    private void UpdateMenuState(RuntimeState state)
    {
        if (_statusItem != null)
        {
            if (state == RuntimeState.Paused)
            {
                var desc = _controller.PauseDescription;
                _statusItem.Text = desc ?? "Status: Paused";
                UpdateStatusIcon(ModernTrayPalette.Muted);
            }
            else
            {
                _statusItem.Text = "Status: Running";
                UpdateStatusIcon(ModernTrayPalette.StatusRunning);
            }
        }

        if (_pauseResumeItem != null)
        {
            _pauseResumeItem.Text = state == RuntimeState.Paused ? "Resume" : "Pause";
        }
    }

    /// <summary>
    /// Swaps the status item's dot icon. The previous bitmap is disposed because
    /// <see cref="ModernTrayIcons.StatusDot"/> is generated on demand (its color
    /// varies with the runtime state) and is never taken from the shared cache.
    /// </summary>
    private void UpdateStatusIcon(System.Drawing.Color color)
    {
        if (_statusItem == null)
        {
            return;
        }

        var previous = _statusItem.Image;
        _statusItem.Image = ModernTrayIcons.StatusDot(16, color);
        // The dot generated by the builder at startup is also uncached, so it is
        // safe to dispose the previous reference here.
        if (previous is System.Drawing.Bitmap bmp && !ReferenceEquals(bmp, _statusItem.Image))
        {
            bmp.Dispose();
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

    private void UpdateDirectionalFocusMenu(bool isEnabled)
    {
        if (_directionalFocusItem != null)
        {
            _directionalFocusItem.Checked = isEnabled;
        }
    }

    private void UpdateRepositionMaximizedMenu(bool isEnabled)
    {
        if (_repositionMaximizedItem != null)
        {
            _repositionMaximizedItem.Checked = isEnabled;
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
