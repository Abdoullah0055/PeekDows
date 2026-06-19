using System;
using System.Windows.Forms;
using PeekDows.Core.Models;

namespace PeekDows.App.Tray;

public class TrayMenuBuilder
{
    private readonly IPeekDowsController _controller;
    private readonly TrayIconController _trayIcon;

    public TrayMenuBuilder(IPeekDowsController controller, TrayIconController trayIcon)
    {
        _controller = controller;
        _trayIcon = trayIcon;
    }

    public ContextMenuStrip Build()
    {
        var menu = new ContextMenuStrip();

        var statusItem = new ToolStripMenuItem("Status: Running") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var arrangeNowItem = new ToolStripMenuItem("Arrange Now");
        arrangeNowItem.Click += (s, e) => _controller.ArrangeNow();
        menu.Items.Add(arrangeNowItem);

        var pauseResumeItem = new ToolStripMenuItem("Pause");
        pauseResumeItem.Click += (s, e) => _controller.TogglePause();
        menu.Items.Add(pauseResumeItem);

        var pauseForMenu = new ToolStripMenuItem("Pause for");

        var pause5Item = new ToolStripMenuItem("5 minutes");
        pause5Item.Click += (s, e) => _controller.PauseFor(TimeSpan.FromMinutes(5));
        pauseForMenu.DropDownItems.Add(pause5Item);

        var pause15Item = new ToolStripMenuItem("15 minutes");
        pause15Item.Click += (s, e) => _controller.PauseFor(TimeSpan.FromMinutes(15));
        pauseForMenu.DropDownItems.Add(pause15Item);

        var pause1HourItem = new ToolStripMenuItem("1 hour");
        pause1HourItem.Click += (s, e) => _controller.PauseFor(TimeSpan.FromHours(1));
        pauseForMenu.DropDownItems.Add(pause1HourItem);

        var pauseUntilResumedItem = new ToolStripMenuItem("Until manually resumed");
        pauseUntilResumedItem.Click += (s, e) => _controller.PauseUntilResumed();
        pauseForMenu.DropDownItems.Add(pauseUntilResumedItem);

        menu.Items.Add(pauseForMenu);

        menu.Items.Add(new ToolStripSeparator());

        var autoArrangeItem = new ToolStripMenuItem("Enable Auto Arrange");
        autoArrangeItem.Click += (s, e) => _controller.ToggleAutoArrange();
        menu.Items.Add(autoArrangeItem);

        var startWithWindowsItem = new ToolStripMenuItem("Start with Windows");
        startWithWindowsItem.Checked = _controller.CurrentSettings.StartWithWindows;
        startWithWindowsItem.Click += (s, e) => _controller.ToggleStartWithWindows();
        menu.Items.Add(startWithWindowsItem);

        var directionalFocusItem = new ToolStripMenuItem("Enable Directional Focus");
        directionalFocusItem.Checked = _controller.CurrentSettings.DirectionalFocusEnabled;
        directionalFocusItem.Click += (s, e) => _controller.ToggleDirectionalFocus();
        menu.Items.Add(directionalFocusItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (s, e) => _trayIcon.OpenSettings();
        menu.Items.Add(settingsItem);

        var openLogItem = new ToolStripMenuItem("Open Log File");
        openLogItem.Click += (s, e) => _controller.OpenLogFile();
        menu.Items.Add(openLogItem);

        var openLogsFolderItem = new ToolStripMenuItem("Open Logs Folder");
        openLogsFolderItem.Click += (s, e) => _controller.OpenLogsFolder();
        menu.Items.Add(openLogsFolderItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => _controller.Exit();
        menu.Items.Add(exitItem);

        _trayIcon.InitializeMenuReferences(statusItem, pauseResumeItem, autoArrangeItem, startWithWindowsItem, directionalFocusItem);

        return menu;
    }
}
