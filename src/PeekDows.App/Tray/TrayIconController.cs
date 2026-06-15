using System;
using System.Drawing;
using System.Windows.Forms;
using PeekDows.App.Settings;

namespace PeekDows.App.Tray;

public class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuBuilder _menuBuilder;
    private SettingsWindow? _settingsWindow;

    public TrayIconController()
    {
        _menuBuilder = new TrayMenuBuilder(this);
        
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PeekDows",
            ContextMenuStrip = _menuBuilder.Build(),
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => OpenSettings();
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

    public void Exit()
    {
        Application.Exit();
    }

    public void ArrangeNow()
    {
        // To be implemented in next batch
    }

    public void Pause()
    {
        // To be implemented in next batch
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _settingsWindow?.Dispose();
    }
}
