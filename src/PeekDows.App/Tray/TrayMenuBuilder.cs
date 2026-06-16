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

        menu.Items.Add(new ToolStripSeparator());

        var openLogItem = new ToolStripMenuItem("Open Log File");
        openLogItem.Click += (s, e) => _controller.OpenLogFile();
        menu.Items.Add(openLogItem);

        var openLogsFolderItem = new ToolStripMenuItem("Open Logs Folder");
        openLogsFolderItem.Click += (s, e) => _controller.OpenLogsFolder();
        menu.Items.Add(openLogsFolderItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (s, e) => _trayIcon.OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => _controller.Exit();
        menu.Items.Add(exitItem);

        _trayIcon.InitializeMenuReferences(statusItem, pauseResumeItem);

        return menu;
    }
}
