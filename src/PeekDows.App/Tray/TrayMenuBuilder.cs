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
            // Generous padding around the whole flyout, matching Win11's
            // breathing room. The renderer paints the hover insets, this controls
            // the outer gutter.
            Padding = new Padding(8, 8, 8, 8),
        };

        // The icon gutter: wide enough for a 16px glyph centered with padding,
        // so text and icon line up cleanly like a native flyout.
        menu.ImageScalingSize = new System.Drawing.Size(16, 16);

        // Apply native Win11 rounded corners once the native handle exists.
        // Opening fires right before display and guarantees the handle is alive.
        menu.Opening += (_, _) =>
        {
            try { ModernTrayWin32.TryApplyRoundedCorners(menu.Handle); }
            catch { /* visual only; rounded corners are best-effort */ }
        };

        var statusItem = new ToolStripMenuItem("Status: Running")
        {
            Enabled = false,
            // StatusTag lets the renderer draw the dot in the right color
            // without any controller coupling. Initial color = running green.
            Tag = new ModernTrayIcons.StatusTag(ModernTrayPalette.StatusRunning),
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var arrangeNowItem = NewItem("Arrange Now", "arrange");
        arrangeNowItem.Click += (s, e) => _controller.ArrangeNow();
        menu.Items.Add(arrangeNowItem);

        var pauseResumeItem = NewItem("Pause", "pause");
        pauseResumeItem.Click += (s, e) => _controller.TogglePause();
        menu.Items.Add(pauseResumeItem);

        var pauseForMenu = NewItem("Pause for", "pause-for");

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

        var windowSizeMenu = NewItem("Window size", "window-size");

        var smallPresetItem = new ToolStripMenuItem("Small — 90%");
        smallPresetItem.Checked = _controller.CurrentWindowSizePreset == WindowSizePreset.Small;
        smallPresetItem.Click += (s, e) => _controller.SetWindowSizePreset(WindowSizePreset.Small);
        windowSizeMenu.DropDownItems.Add(smallPresetItem);

        var mediumPresetItem = new ToolStripMenuItem("Medium — 95%");
        mediumPresetItem.Checked = _controller.CurrentWindowSizePreset == WindowSizePreset.Medium;
        mediumPresetItem.Click += (s, e) => _controller.SetWindowSizePreset(WindowSizePreset.Medium);
        windowSizeMenu.DropDownItems.Add(mediumPresetItem);

        var largePresetItem = new ToolStripMenuItem("Large — 98%");
        largePresetItem.Checked = _controller.CurrentWindowSizePreset == WindowSizePreset.Large;
        largePresetItem.Click += (s, e) => _controller.SetWindowSizePreset(WindowSizePreset.Large);
        windowSizeMenu.DropDownItems.Add(largePresetItem);

        menu.Items.Add(windowSizeMenu);

        menu.Items.Add(new ToolStripSeparator());

        var autoArrangeItem = NewItem("Enable Auto Arrange", "auto-arrange");
        autoArrangeItem.Click += (s, e) => _controller.ToggleAutoArrange();
        menu.Items.Add(autoArrangeItem);

        var repositionMaximizedItem = NewItem("Reposition maximized windows", "reposition-maximized");
        repositionMaximizedItem.Checked = _controller.CurrentSettings.AllowRepositionMaximizedWindows;
        repositionMaximizedItem.Click += (s, e) => _controller.ToggleAllowRepositionMaximizedWindows();
        menu.Items.Add(repositionMaximizedItem);

        var startWithWindowsItem = NewItem("Start with Windows", "start-windows");
        startWithWindowsItem.Checked = _controller.CurrentSettings.StartWithWindows;
        startWithWindowsItem.Click += (s, e) => _controller.ToggleStartWithWindows();
        menu.Items.Add(startWithWindowsItem);

        var directionalFocusItem = NewItem("Enable Directional Focus", "directional-focus");
        directionalFocusItem.Checked = _controller.CurrentSettings.DirectionalFocusEnabled;
        directionalFocusItem.Click += (s, e) => _controller.ToggleDirectionalFocus();
        menu.Items.Add(directionalFocusItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = NewItem("Settings", "settings");
        settingsItem.Click += (s, e) => _trayIcon.OpenSettings();
        menu.Items.Add(settingsItem);

        var openLogItem = NewItem("Open Log File", "log-file");
        openLogItem.Click += (s, e) => _controller.OpenLogFile();
        menu.Items.Add(openLogItem);

        var openLogsFolderItem = NewItem("Open Logs Folder", "logs-folder");
        openLogsFolderItem.Click += (s, e) => _controller.OpenLogsFolder();
        menu.Items.Add(openLogsFolderItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = NewItem("Exit", "exit");
        exitItem.Click += (s, e) => _controller.Exit();
        menu.Items.Add(exitItem);

        _trayIcon.InitializeMenuReferences(statusItem, pauseResumeItem, autoArrangeItem, repositionMaximizedItem, startWithWindowsItem, directionalFocusItem, smallPresetItem, mediumPresetItem, largePresetItem);

        return menu;
    }

    /// <summary>
    /// Builds a menu item whose icon is rendered on demand by the custom renderer
    /// via the Tag key (a Segoe Fluent Icons glyph). Keeping icon assignment in
    /// one place means every item shares identical sizing, and there is no bitmap
    /// to generate, cache or scale.
    /// </summary>
    private static ToolStripMenuItem NewItem(string text, string iconKey)
    {
        return new ToolStripMenuItem(text)
        {
            Tag = iconKey,
            // Reserve the image cell so the renderer has a target rect to paint
            // the glyph into; Image itself stays null.
            ImageScaling = ToolStripItemImageScaling.SizeToFit,
        };
    }
}
