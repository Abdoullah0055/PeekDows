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
        var menu = new ContextMenuStrip
        {
            Renderer = new ModernTrayRenderer(),
            ShowImageMargin = true,
            BackColor = ModernTrayPalette.Background,
            ForeColor = ModernTrayPalette.Text,
            Padding = new Padding(2, 6, 2, 6),
        };

        // Apply native Win11 rounded corners once the native handle exists.
        // The handle is lazily created; Opening fires right before display and
        // guarantees the handle is alive.
        menu.Opening += (_, _) =>
        {
            try { ModernTrayWin32.TryApplyRoundedCorners(menu.Handle); }
            catch { /* visual only; rounded corners are best-effort */ }
        };

        int iconSize = 16;

        var statusItem = new ToolStripMenuItem("Status: Running")
        {
            Enabled = false,
            ImageScaling = ToolStripItemImageScaling.None,
            Image = ModernTrayIcons.StatusDot(iconSize, ModernTrayPalette.StatusRunning),
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var arrangeNowItem = NewItem("Arrange Now", "arrange", iconSize);
        arrangeNowItem.Click += (s, e) => _controller.ArrangeNow();
        menu.Items.Add(arrangeNowItem);

        var pauseResumeItem = NewItem("Pause", "pause", iconSize);
        pauseResumeItem.Click += (s, e) => _controller.TogglePause();
        menu.Items.Add(pauseResumeItem);

        var pauseForMenu = NewItem("Pause for", "pause-for", iconSize);

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

        var autoArrangeItem = NewItem("Enable Auto Arrange", "auto-arrange", iconSize);
        autoArrangeItem.Click += (s, e) => _controller.ToggleAutoArrange();
        menu.Items.Add(autoArrangeItem);

        var repositionMaximizedItem = NewItem("Reposition maximized windows", "reposition-maximized", iconSize);
        repositionMaximizedItem.Checked = _controller.CurrentSettings.AllowRepositionMaximizedWindows;
        repositionMaximizedItem.Click += (s, e) => _controller.ToggleAllowRepositionMaximizedWindows();
        menu.Items.Add(repositionMaximizedItem);

        var startWithWindowsItem = NewItem("Start with Windows", "start-windows", iconSize);
        startWithWindowsItem.Checked = _controller.CurrentSettings.StartWithWindows;
        startWithWindowsItem.Click += (s, e) => _controller.ToggleStartWithWindows();
        menu.Items.Add(startWithWindowsItem);

        var directionalFocusItem = NewItem("Enable Directional Focus", "directional-focus", iconSize);
        directionalFocusItem.Checked = _controller.CurrentSettings.DirectionalFocusEnabled;
        directionalFocusItem.Click += (s, e) => _controller.ToggleDirectionalFocus();
        menu.Items.Add(directionalFocusItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = NewItem("Settings", "settings", iconSize);
        settingsItem.Click += (s, e) => _trayIcon.OpenSettings();
        menu.Items.Add(settingsItem);

        var openLogItem = NewItem("Open Log File", "log-file", iconSize);
        openLogItem.Click += (s, e) => _controller.OpenLogFile();
        menu.Items.Add(openLogItem);

        var openLogsFolderItem = NewItem("Open Logs Folder", "logs-folder", iconSize);
        openLogsFolderItem.Click += (s, e) => _controller.OpenLogsFolder();
        menu.Items.Add(openLogsFolderItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = NewItem("Exit", "exit", iconSize);
        exitItem.Click += (s, e) => _controller.Exit();
        menu.Items.Add(exitItem);

        _trayIcon.InitializeMenuReferences(statusItem, pauseResumeItem, autoArrangeItem, repositionMaximizedItem, startWithWindowsItem, directionalFocusItem);

        return menu;
    }

    /// <summary>
    /// Builds a menu item with a monochrome icon and consistent scaling. Keeping
    /// the icon assignment in one place means every item shares the same sizing
    /// and the renderer's <c>OnRenderItemImage</c> applies uniformly.
    /// </summary>
    private static ToolStripMenuItem NewItem(string text, string iconKey, int iconSize)
    {
        return new ToolStripMenuItem(text)
        {
            Image = ModernTrayIcons.Get(iconKey, iconSize),
            ImageScaling = ToolStripItemImageScaling.None,
        };
    }
}
