using System;
using System.Drawing;
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
    private SettingsWindow? _settingsWindow;

    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _pauseResumeItem;

    public TrayIconController(IPeekDowsController controller) : this(controller, null) { }

    public TrayIconController(IPeekDowsController controller, FileLogger? logger)
    {
        _controller = controller;
        _logger = logger;
        _menuBuilder = new TrayMenuBuilder(controller, this);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PeekDows",
            ContextMenuStrip = _menuBuilder.Build(),
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => _controller.OpenSettings();

        _controller.StateChanged += OnStateChanged;

        UpdateMenuState(_controller.State);
    }

    internal void InitializeMenuReferences(ToolStripMenuItem statusItem, ToolStripMenuItem pauseResumeItem)
    {
        _statusItem = statusItem;
        _pauseResumeItem = pauseResumeItem;
        UpdateMenuState(_controller.State);
    }

    public void OpenSettings()
    {
        if (_settingsWindow == null || _settingsWindow.IsDisposed)
        {
            _settingsWindow = new SettingsWindow();
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

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _settingsWindow?.Dispose();
    }
}
