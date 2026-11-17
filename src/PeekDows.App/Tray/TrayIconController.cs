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
    internal void NotifyCustomFallback(int count) => _notifyIcon.ShowBalloonTip(4000, "PeekDows custom zones",
        $"{count} window(s) could not fit their zones and used the full work area.", ToolTipIcon.Info);

    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuBuilder _menuBuilder;
    private readonly IPeekDowsController _controller;
    private readonly FileLogger? _logger;
    private readonly Icon _trayIcon;
    private readonly bool _ownsTrayIcon;
    private readonly SettingsService? _settingsService;
    private Form? _settingsWindow;

    public TrayIconController(IPeekDowsController controller) : this(controller, null) { }

    public TrayIconController(IPeekDowsController controller, FileLogger? logger) : this(controller, logger, null) { }

    public TrayIconController(IPeekDowsController controller, FileLogger? logger, SettingsService? settingsService)
    {
        _controller = controller;
        _logger = logger;
        _settingsService = settingsService;
        _menuBuilder = new TrayMenuBuilder(controller);

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
        OnStateChanged(_controller.State);
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

            var window = host ?? new SettingsLegacyWindow(settings, settingsService, _controller);
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

        var legacy = new SettingsLegacyWindow(settings, settingsService, _controller);
        legacy.SettingsSaved += () => _controller.OnSettingsChanged();
        _settingsWindow = legacy;
        legacy.Show();
    }

    private void OnStateChanged(RuntimeState state)
    {
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

    public void Dispose()
    {
        // C6 fix: unsubscribe events so controller doesn't keep us alive (hot reload / tests).
        try { _controller.StateChanged -= OnStateChanged; } catch { }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_ownsTrayIcon)
        {
            _trayIcon.Dispose();
        }
        _settingsWindow?.Dispose();
    }
}
