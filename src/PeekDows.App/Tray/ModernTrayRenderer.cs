using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PeekDows.App.Tray;

/// <summary>
/// Modern dark-theme renderer for the tray menu, DPI-aware and using Segoe Fluent
/// Icons glyphs instead of pre-rendered bitmaps. Subclasses
/// <see cref="ToolStripProfessionalRenderer"/> so we inherit layout, image/check
/// margins and recursive submenu rendering, while overriding every visible paint
/// method. A single instance attached to the root <see cref="ContextMenuStrip"/>
/// also styles its dropdowns automatically.
/// </summary>
internal sealed class ModernTrayRenderer : ToolStripProfessionalRenderer
{
    // Base (96 DPI) metrics. Everything is multiplied by the live DPI scale at
    // paint time so 100/125/150% all render crisp and proportionally correct.
    private const float BaseHoverRadius = 6f;
    private const float BaseHoverInsetX = 4f;
    private const float BaseHoverInsetY = 2f;
    private const float BaseCheckRadius = 4f;
    private const float BaseTickStroke = 1.8f;
    private const float BaseArrowStroke = 1.6f;
    private const float BaseSeparatorInset = 16f;

    public ModernTrayRenderer() : base(new EmptyColorTable())
    {
        // We draw rounded selection rectangles ourselves; turn off the built-in
        // rounded-edges path so the system focus rectangle doesn't double up.
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var bg = new SolidBrush(ModernTrayPalette.Background);
        e.Graphics.FillRectangle(bg, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Subtle 1px border inset by 0.5px to avoid anti-alias bleed. The OS paints
        // the drop shadow separately.
        var r = e.AffectedBounds;
        var rect = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
        using var pen = new Pen(ModernTrayPalette.Border);
        e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!(item.Selected || item.Pressed))
        {
            return;
        }

        float scale = ScaleFor(e.ToolStrip);
        var bounds = new RectangleF(Point.Empty, item.Size);

        var inset = new RectangleF(
            bounds.X + BaseHoverInsetX * scale,
            bounds.Y + BaseHoverInsetY * scale,
            bounds.Width - BaseHoverInsetX * 2f * scale,
            bounds.Height - BaseHoverInsetY * 2f * scale);

        using var path = RoundedRect(inset, BaseHoverRadius * scale);
        using var brush = new SolidBrush(ModernTrayPalette.Hover);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Force a crisp text rendering mode and a clean color. WinForms' default
        // text path uses SingleBitPerPixel under some conditions which looks soft
        // on dark backgrounds.
        var color = e.Item.Enabled ? ModernTrayPalette.Text : ModernTrayPalette.Muted;
        e.TextColor = color;

        var prevHint = e.Graphics.TextRenderingHint;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        try
        {
            base.OnRenderItemText(e);
        }
        finally
        {
            e.Graphics.TextRenderingHint = prevHint;
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float scale = ScaleFor(e.ToolStrip);
        var g = e.Graphics;
        var bounds = e.Item.ContentRectangle;
        using var pen = new Pen(ModernTrayPalette.Separator, 1f);

        if (e.Vertical)
        {
            float x = bounds.Left + bounds.Width / 2f;
            g.DrawLine(pen, x, bounds.Top + 2 * scale, x, bounds.Bottom - 2 * scale);
        }
        else
        {
            float y = bounds.Top + bounds.Height / 2f;
            float inset = BaseSeparatorInset * scale;
            g.DrawLine(pen, bounds.Left + inset, y, bounds.Right - inset, y);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Modern check: rounded accent square with a white tick, replacing the
        // legacy checkmark glyph entirely.
        float scale = ScaleFor(e.ToolStrip);
        var g = e.Graphics;
        var cell = e.ImageRectangle;
        if (cell.Width <= 0 || cell.Height <= 0)
        {
            return;
        }

        // Square sized to ~70% of the cell, centered.
        float side = (System.MathF.Min(cell.Width, cell.Height)) * 0.72f;
        var square = new RectangleF(
            cell.X + (cell.Width - side) / 2f,
            cell.Y + (cell.Height - side) / 2f,
            side,
            side);

        using var path = RoundedRect(square, BaseCheckRadius * scale);
        using var accent = new SolidBrush(ModernTrayPalette.Accent);
        g.FillPath(accent, path);

        using var tick = new Pen(ModernTrayPalette.AccentForeground, BaseTickStroke * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        var pad = side * 0.26f;
        g.DrawLines(tick, new PointF[]
        {
            new(square.Left + pad, square.Top + side * 0.52f),
            new(square.Left + side * 0.42f, square.Bottom - pad),
            new(square.Right - pad, square.Top + side * 0.30f),
        });
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        // Redraw the submenu chevron in the muted text color; the default arrow
        // uses the system menu color which clashes with the dark theme.
        var r = e.ArrowRectangle;
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float scale = ScaleFor(e.Item?.Owner as ToolStrip);
        var g = e.Graphics;
        using var pen = new Pen(ModernTrayPalette.Muted, BaseArrowStroke * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        var midY = r.Y + r.Height / 2f;
        g.DrawLines(pen, new PointF[]
        {
            new(r.Left + r.Width * 0.30f, r.Top + r.Height * 0.28f),
            new(r.Right - r.Width * 0.28f, midY),
            new(r.Left + r.Width * 0.30f, r.Bottom - r.Height * 0.28f),
        });
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        // Draw the glyph directly as vector text into the cell — this is the fix
        // for the previous blurry look (no intermediate bitmap, no upscaling).
        var cell = e.ImageRectangle;
        if (cell.Width <= 0 || cell.Height <= 0)
        {
            return;
        }

        var item = e.Item;
        float scale = ScaleFor(e.ToolStrip);
        var color = item.Enabled ? ModernTrayPalette.Text : ModernTrayPalette.Muted;

        // Status row carries a StatusTag (dot color) instead of an icon key.
        if (item.Tag is ModernTrayIcons.StatusTag statusTag)
        {
            ModernTrayIcons.DrawStatusDot(e.Graphics, cell, statusTag.Color, scale);
            return;
        }

        if (item.Tag is string tag && ModernTrayIcons.IsIconKey(tag))
        {
            ModernTrayIcons.DrawIconGlyph(e.Graphics, tag, cell, color, scale);
            return;
        }

        // Fallback: if a real Image was assigned (e.g. legacy code), draw it
        // crisply without soft bicubic scaling.
        if (e.Image != null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(e.Image, cell);
        }
    }

    /// <summary>
    /// DPI scale factor for the tool strip being painted. Clamped to a sane range
    /// so an unexpected 0 or huge DPI can't blow up metrics.
    /// </summary>
    private static float ScaleFor(ToolStrip? toolStrip)
    {
        int dpi = 96;
        try
        {
            if (toolStrip != null)
            {
                dpi = toolStrip.DeviceDpi;
            }
        }
        catch
        {
            dpi = 96;
        }

        if (dpi <= 0)
        {
            dpi = 96;
        }

        float scale = dpi / 96f;
        if (scale < 1f) scale = 1f;
        if (scale > 3f) scale = 3f;
        return scale;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// A near-empty color table. We override every visible render method, but
    /// <c>ToolStripProfessionalRenderer</c> still consults the table for the image
    /// margin strip and a few gradients. Forcing those to the menu background
    /// avoids stray light strips next to the icons.
    /// </summary>
    private sealed class EmptyColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => ModernTrayPalette.Background;
        public override Color ToolStripGradientMiddle => ModernTrayPalette.Background;
        public override Color ToolStripGradientEnd => ModernTrayPalette.Background;
        public override Color MenuStripGradientBegin => ModernTrayPalette.Background;
        public override Color MenuStripGradientEnd => ModernTrayPalette.Background;
        public override Color ImageMarginGradientBegin => ModernTrayPalette.Background;
        public override Color ImageMarginGradientMiddle => ModernTrayPalette.Background;
        public override Color ImageMarginGradientEnd => ModernTrayPalette.Background;
        public override Color MenuBorder => ModernTrayPalette.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color ToolStripBorder => ModernTrayPalette.Border;
        public override Color SeparatorDark => ModernTrayPalette.Separator;
        public override Color SeparatorLight => ModernTrayPalette.Separator;
        public override Color StatusStripGradientBegin => ModernTrayPalette.Background;
        public override Color StatusStripGradientEnd => ModernTrayPalette.Background;
    }
}
