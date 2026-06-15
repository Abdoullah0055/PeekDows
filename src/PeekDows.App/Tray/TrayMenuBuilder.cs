using System.Windows.Forms;

namespace PeekDows.App.Tray;

public class TrayMenuBuilder
{
    private readonly TrayIconController _controller;

    public TrayMenuBuilder(TrayIconController controller)
    {
        _controller = controller;
    }

    public ContextMenuStrip Build()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Status: Running") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        
        var arrangeNowItem = new ToolStripMenuItem("Arrange Now");
        arrangeNowItem.Click += (s, e) => _controller.ArrangeNow();
        menu.Items.Add(arrangeNowItem);

        var pauseItem = new ToolStripMenuItem("Pause");
        pauseItem.Click += (s, e) => _controller.Pause();
        menu.Items.Add(pauseItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (s, e) => _controller.OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => _controller.Exit();
        menu.Items.Add(exitItem);

        return menu;
    }
}
