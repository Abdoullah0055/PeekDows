using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PeekDows.App.Tray;

/// <summary>
/// Modern dark-theme renderer for the tray menu. Subclasses
/// <see cref="ToolStripProfessionalRenderer"/> so we inherit the layout engine,
/// image/check margins and recursive submenu rendering, while overriding every
/// paint method that draws a visible surface. A single instance attached to the
/// root <see cref="ContextMenuStrip"/> also styles its dropdowns automatically.
/// </summary>
internal sealed class ModernTrayRenderer : ToolStripProfessionalRenderer
{
    // Rounded-corner radius for hover backgrounds and check boxes, in pixels.
    private const float Radius = 6f;

    public ModernTrayRenderer() : base(new EmptyColorTable())
    {
        // We draw rounded selection rectangles ourselves; disable the system
        // focus rectangle which would otherwise draw a dotted border on top.
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        using var bg = new SolidBrush(ModernTrayPalette.Background);
        g.FillRectangle(bg, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Draw a subtle 1px border around the menu surface (not the drop shadow,
        // which Windows paints natively). Inset by 0.5px to avoid anti-alias bleed.
        var r = e.AffectedBounds;
        var rect = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
        using var pen = new Pen(ModernTrayPalette.Border);
        e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var g = e.Graphics;
        var bounds = new RectangleF(Point.Empty, item.Size);

        bool highlighted = item.Selected || item.Pressed;
        if (!highlighted)
        {
            return;
        }

        // Inset the hover rect so adjacent items have a visible gap, matching
        // the Windows 11 flyout look.
        var inset = new RectangleF(
            bounds.X + 4f,
            bounds.Y + 2f,
            bounds.Width - 8f,
            bounds.Height - 4f);

        using var path = RoundedRect(inset, Radius);
        using var brush = new SolidBrush(ModernTrayPalette.Hover);
        g.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Disabled items (e.g. the Status line) and the status muted look use
        // the muted color; everything else uses the foreground text color.
        var color = e.Item.Enabled ? ModernTrayPalette.Text : ModernTrayPalette.Muted;
        e.TextColor = color;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        var vertical = e.Vertical;
        var bounds = e.Item.ContentRectangle;

        using var pen = new Pen(ModernTrayPalette.Separator, 1f);

        if (vertical)
        {
            float x = bounds.Left + bounds.Width / 2f;
            g.DrawLine(pen, x, bounds.Top + 2, x, bounds.Bottom - 2);
        }
        else
        {
            // Horizontal separators get horizontal insets so the line doesn't
            // touch the menu border.
            float y = bounds.Top + bounds.Height / 2f;
            g.DrawLine(pen, bounds.Left + 12, y, bounds.Right - 12, y);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Modern check: a rounded accent square with a white tick. Replaces the
        // legacy WinForms checkmark glyph entirely.
        var g = e.Graphics;
        var bounds = new RectangleF(e.ImageRectangle.Location, e.ImageRectangle.Size);

        // Square, centered vertically in the image cell, ~14px logical.
        float side = System.MathF.Min(bounds.Width, bounds.Height) - 2f;
        if (side <= 0) side = 14f;
        var square = new RectangleF(
            bounds.X + (bounds.Width - side) / 2f,
            bounds.Y + (bounds.Height - side) / 2f,
            side,
            side);

        using var path = RoundedRect(square, 4f);
        using var accent = new SolidBrush(ModernTrayPalette.Accent);
        g.FillPath(accent, path);

        // White tick centered in the square.
        using var tick = new Pen(ModernTrayPalette.AccentForeground, 1.8f)
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
        // Redraw the submenu chevron in the muted text color (the default arrow
        // uses the system menu color which clashes with the dark theme). We draw
        // directly rather than going through base.OnRenderArrow so the color and
        // stroke style are fully under our control.
        var r = e.ArrowRectangle;
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        var g = e.Graphics;
        using var pen = new Pen(ModernTrayPalette.Muted, 1.6f)
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
        // Draw the monochrome icons as-is. We rely on the image already being
        // pre-rendered in the foreground color by ModernTrayIcons.
        if (e.Image == null)
        {
            return;
        }

        var g = e.Graphics;
        var r = e.ImageRectangle;
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        // Disabled items (status row) get a dimmed icon.
        if (!e.Item.Enabled)
        {
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.5f };
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);
            var destPoints = new PointF[]
            {
                new(r.Left, r.Top),
                new(r.Right, r.Top),
                new(r.Left, r.Bottom),
            };
            g.DrawImage(e.Image, destPoints, new RectangleF(0, 0, e.Image.Width, e.Image.Height), GraphicsUnit.Pixel, ia);
        }
        else
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(e.Image, r);
        }
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
    /// An essentially transparent color table. We override every visible render
    /// method ourselves, but <c>ToolStripProfessionalRenderer</c> still consults
    /// the color table for a few leftover details (e.g. the image margin strip and
    /// margin gradient). Forcing everything to the menu background avoids stray
    /// light strips next to the icons.
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
