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
    private Form? _settingsWindow;

    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _pauseResumeItem;
    private ToolStripMenuItem? _autoArrangeItem;
    private ToolStripMenuItem? _repositionMaximizedItem;
    private ToolStripMenuItem? _startWithWindowsItem;
    private ToolStripMenuItem? _directionalFocusItem;
    private ToolStripMenuItem? _animateTransitionsItem;
    private ToolStripMenuItem? _smallPresetItem;
    private ToolStripMenuItem? _mediumPresetItem;
    private ToolStripMenuItem? _largePresetItem;

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
        _controller.AnimateWindowTransitionsChanged += OnAnimateTransitionsChanged;
        _controller.AllowRepositionMaximizedWindowsChanged += OnAllowRepositionMaximizedWindowsChanged;
        _controller.WindowSizePresetChanged += OnWindowSizePresetChanged;

        UpdateMenuState(_controller.State);
        UpdateAutoArrangeMenu(_controller.CurrentSettings?.AutoArrange ?? false);
        UpdateStartWithWindowsMenu(_controller.CurrentSettings?.StartWithWindows ?? false);
        UpdateDirectionalFocusMenu(_controller.CurrentSettings?.DirectionalFocusEnabled ?? false);
        UpdateAnimateTransitionsMenu(_controller.CurrentSettings?.AnimateWindowTransitions ?? true);
        UpdateRepositionMaximizedMenu(_controller.CurrentSettings?.AllowRepositionMaximizedWindows ?? false);
    }

    internal void InitializeMenuReferences(ToolStripMenuItem statusItem, ToolStripMenuItem pauseResumeItem, ToolStripMenuItem autoArrangeItem, ToolStripMenuItem repositionMaximizedItem, ToolStripMenuItem startWithWindowsItem, ToolStripMenuItem directionalFocusItem, ToolStripMenuItem animateTransitionsItem, ToolStripMenuItem smallPresetItem, ToolStripMenuItem mediumPresetItem, ToolStripMenuItem largePresetItem)
    {
        _statusItem = statusItem;
        _pauseResumeItem = pauseResumeItem;
        _autoArrangeItem = autoArrangeItem;
        _repositionMaximizedItem = repositionMaximizedItem;
        _startWithWindowsItem = startWithWindowsItem;
        _directionalFocusItem = directionalFocusItem;
        _animateTransitionsItem = animateTransitionsItem;
        _smallPresetItem = smallPresetItem;
        _mediumPresetItem = mediumPresetItem;
        _largePresetItem = largePresetItem;
        UpdateMenuState(_controller.State);
        UpdateAutoArrangeMenu(_controller.CurrentSettings?.AutoArrange ?? false);
        UpdateStartWithWindowsMenu(_controller.CurrentSettings?.StartWithWindows ?? false);
        UpdateDirectionalFocusMenu(_controller.CurrentSettings?.DirectionalFocusEnabled ?? false);
        UpdateAnimateTransitionsMenu(_controller.CurrentSettings?.AnimateWindowTransitions ?? true);
        UpdateRepositionMaximizedMenu(_controller.CurrentSettings?.AllowRepositionMaximizedWindows ?? false);
        UpdateWindowSizePresetMenu(_controller.CurrentSettings?.WindowSizePreset ?? WindowSizePreset.Small);
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
            var settingsService = _settingsService ?? new SettingsService();

            // Modern WebView2 UI first; fall back to the legacy window when the runtime
            // is unavailable so settings access is never lost.
            Form? host = null;
            try
            {
                host = SettingsHostForm.TryCreate(_controller, settingsService, _logger, out var reason);
                if (host == null)
                    _logger?.Warn($"Settings window: WebView2 unavailable ({reason}), using legacy window");
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Settings window: WebView2 host creation failed ({ex.Message}), using legacy window");
            }

            var window = host ?? new SettingsLegacyWindow(settings, settingsService);
            if (host is SettingsHostForm hostForm)
            {
                hostForm.InitializationFailed += reason =>
                    ReplaceFailedSettingsHost(hostForm, settings, settingsService, reason);
            }
            else if (window is SettingsLegacyWindow legacy)
            {
                legacy.SettingsSaved += () => _controller.OnSettingsChanged();
            }

            _settingsWindow = window;
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

    private void ReplaceFailedSettingsHost(
        SettingsHostForm host,
        AppSettings settings,
        SettingsService settingsService,
        string reason)
    {
        _logger?.Warn($"Settings window: WebView2 initialization failed ({reason}), using legacy window");

        if (!ReferenceEquals(_settingsWindow, host))
            return;

        var legacy = new SettingsLegacyWindow(settings, settingsService);
        legacy.SettingsSaved += () => _controller.OnSettingsChanged();
        _settingsWindow = legacy;
        legacy.Show();
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

    private void OnAnimateTransitionsChanged(bool isEnabled)
    {
        UpdateAnimateTransitionsMenu(isEnabled);
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
    /// Updates the status dot color by mutating the renderer-facing
    /// <see cref="ModernTrayIcons.StatusTag"/> carried in the item's Tag, then
    /// invalidating so the glyph repaints. No bitmap is generated or disposed —
    /// the dot is drawn vectorially by the renderer, which keeps it crisp at any
    /// DPI and avoids the previous per-state allocation/dispose churn.
    /// </summary>
    private void UpdateStatusIcon(System.Drawing.Color color)
    {
        if (_statusItem == null)
        {
            return;
        }

        if (_statusItem.Tag is ModernTrayIcons.StatusTag statusTag)
        {
            if (statusTag.Color != color)
            {
                statusTag.Color = color;
                _statusItem.Invalidate();
            }
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

    private void UpdateAnimateTransitionsMenu(bool isEnabled)
    {
        if (_animateTransitionsItem != null)
        {
            _animateTransitionsItem.Checked = isEnabled;
        }
    }

    private void UpdateRepositionMaximizedMenu(bool isEnabled)
    {
        if (_repositionMaximizedItem != null)
        {
            _repositionMaximizedItem.Checked = isEnabled;
        }
    }

    private void OnWindowSizePresetChanged(WindowSizePreset preset)
    {
        UpdateWindowSizePresetMenu(preset);
    }

    private void UpdateWindowSizePresetMenu(WindowSizePreset preset)
    {
        if (_smallPresetItem != null)
            _smallPresetItem.Checked = preset == WindowSizePreset.Small;
        if (_mediumPresetItem != null)
            _mediumPresetItem.Checked = preset == WindowSizePreset.Medium;
        if (_largePresetItem != null)
            _largePresetItem.Checked = preset == WindowSizePreset.Large;
    }

    public void Dispose()
    {
        // C6 fix: unsubscribe events so controller doesn't keep us alive (hot reload / tests).
        try { _controller.StateChanged -= OnStateChanged; } catch { }
        try { _controller.AutoArrangeChanged -= OnAutoArrangeChanged; } catch { }
        try { _controller.StartWithWindowsChanged -= OnStartWithWindowsChanged; } catch { }
        try { _controller.DirectionalFocusChanged -= OnDirectionalFocusChanged; } catch { }
        try { _controller.AnimateWindowTransitionsChanged -= OnAnimateTransitionsChanged; } catch { }
        try { _controller.AllowRepositionMaximizedWindowsChanged -= OnAllowRepositionMaximizedWindowsChanged; } catch { }
        try { _controller.WindowSizePresetChanged -= OnWindowSizePresetChanged; } catch { }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_ownsTrayIcon)
        {
            _trayIcon.Dispose();
        }
        _settingsWindow?.Dispose();
    }
}
