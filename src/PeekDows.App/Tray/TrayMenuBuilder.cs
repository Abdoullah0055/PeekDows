using System.Windows.Forms;
using PeekDows.Core.Models;

namespace PeekDows.App.Tray;

public class TrayMenuBuilder
{
    private readonly IPeekDowsController _controller;

    public TrayMenuBuilder(IPeekDowsController controller)
    {
        _controller = controller;
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

        var arrangeNowItem = NewItem("Arrange Now", "arrange");
        arrangeNowItem.Click += (_, _) => _controller.ArrangeNow();

        var undoArrangeItem = NewItem("Undo arrange", "undo");
        undoArrangeItem.Click += (_, _) => _controller.UndoArrange();

        var pauseResumeItem = NewItem("Pause", "pause");
        pauseResumeItem.Click += (_, _) => _controller.TogglePause();

        var settingsItem = NewItem("Settings", "settings");
        settingsItem.Click += (_, _) => _controller.OpenSettings();

        var exitItem = NewItem("Exit", "exit");
        exitItem.Click += (_, _) => _controller.Exit();

        menu.Items.AddRange([
            arrangeNowItem, undoArrangeItem, pauseResumeItem,
            new ToolStripSeparator(), settingsItem, new ToolStripSeparator(), exitItem
        ]);
        menu.Opening += (_, _) =>
        {
            var paused = _controller.State == RuntimeState.Paused;
            arrangeNowItem.Enabled = !paused && _controller.CurrentSettings.Enabled;
            undoArrangeItem.Available = _controller.CanUndoArrange;
            pauseResumeItem.Text = paused ? "Resume" : "Pause";
            pauseResumeItem.Tag = paused ? "resume" : "pause";
        };

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
