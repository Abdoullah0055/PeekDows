using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PeekDows.App.Tray;

/// <summary>
/// Renders the tray menu's icons using Windows 11 system icon fonts
/// (Segoe Fluent Icons, with Segoe MDL2 Assets fallback). Drawing the glyphs as
/// vector text — instead of pre-rendering bitmaps that get upscaled — keeps them
/// crisp at every DPI and visually consistent with native Win11 flyouts.
/// </summary>
internal static class ModernTrayIcons
{
    /// <summary>
    /// Item key -> Segoe Fluent Icons codepoint. All codes are from Microsoft's
    /// official "Segoe Fluent Icons" glyph map (the same font Windows 11 uses for
    /// its context menus and flyouts). Comments name the official glyph so future
    /// tweaks are easy.
    /// </summary>
    private static readonly Dictionary<string, int> Glyphs = new()
    {
        // E8B7 = "ViewAll" (overlapping tiles — matches PeekDows' peek/stack concept)
        { "arrange",             0xE8B7 },
        // E769 = "Pause"
        { "pause",               0xE769 },
        // E7AE = "Clock" (time-bounded pause)
        { "pause-for",           0xE7AE },
        // E72C = "RepeatAll" / auto cycle
        { "auto-arrange",        0xE777 },
        // E740 = "ChromeMaximize" (window going maximized)
        { "reposition-maximized",0xE947 },
        // E782 = Windows logo glyph
        { "start-windows",       0xE782 },
        // E7B0 = "Arrange" (directional layout)
        { "directional-focus",   0xE7B0 },
        // E768 = "Play" (motion/animation)
        { "animate-transitions", 0xE768 },
        // E713 = "Setting" (the canonical Win11 gear)
        { "settings",            0xE713 },
        // E8A5 = "Document"
        { "log-file",            0xE8A5 },
        // ED25 = "FolderOpen"
        { "logs-folder",         0xED25 },
        // E7E8 = "PowerButton"
        { "exit",                0xE7E8 },
    };

    private static readonly string IconFontFamily = ResolveIconFontFamily();

    /// <summary>
    /// True when the menu item key is a known glyph key handled by
    /// <see cref="DrawIconGlyph"/>. The status row uses a different key ("status")
    /// and is drawn separately.
    /// </summary>
    public static bool IsIconKey(object? tag) => tag is string s && Glyphs.ContainsKey(s);

    /// <summary>
    /// Tag payload for the status-row item. Carries the dot color (which changes
    /// with the runtime state: green when running, muted when paused) so the
    /// renderer can paint it without knowing about the controller.
    /// </summary>
    public sealed class StatusTag
    {
        public StatusTag(Color color) { Color = color; }
        public Color Color { get; set; }
    }

    /// <summary>
    /// Draws the glyph for the given icon key directly into the target cell at the
    /// cell's native size — no intermediate bitmap, no upscaling. This is the key
    /// fix for the previous blurry/cheap look.
    /// </summary>
    public static void DrawIconGlyph(Graphics g, string key, Rectangle bounds, Color color, float scale)
    {
        if (!Glyphs.TryGetValue(key, out int code))
        {
            return;
        }

        // Fill ~80% of the cell height; the font's vertical metrics add their own
        // padding, so 0.8 lands the glyph visually centered like native flyouts.
        float emSize = bounds.Height * 0.8f * scale;
        if (emSize < 6f)
        {
            emSize = 6f;
        }

        using var font = new Font(IconFontFamily, emSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        var prevHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        try
        {
            g.DrawString(char.ConvertFromUtf32(code), font, brush, bounds, format);
        }
        finally
        {
            g.TextRenderingHint = prevHint;
            format.Dispose();
        }
    }

    /// <summary>
    /// Draws the status indicator dot. Kept as a vector primitive (not a glyph)
    /// because it is the only icon whose color changes at runtime and a flat
    /// circle reads cleaner than any available Fluent dot glyph at this size.
    /// </summary>
    public static void DrawStatusDot(Graphics g, Rectangle bounds, Color color, float scale)
    {
        // Inset so the dot sits comfortably in the icon gutter.
        float pad = 3f * scale;
        float side = (bounds.Width - pad * 2f) * 0.6f;
        if (side < 4f)
        {
            side = 4f;
        }
        var cx = bounds.X + bounds.Width / 2f;
        var cy = bounds.Y + bounds.Height / 2f;
        var rect = new RectangleF(cx - side / 2f, cy - side / 2f, side, side);

        var prevMode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        try
        {
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, rect);
        }
        finally
        {
            g.SmoothingMode = prevMode;
        }
    }

    /// <summary>
    /// Picks the best available system icon font. Segoe Fluent Icons ships with
    /// Windows 11; Segoe MDL2 Assets is the older fallback present on Windows 10.
    /// Tested once and cached for the process lifetime.
    /// </summary>
    private static string ResolveIconFontFamily()
    {
        try
        {
            using var col = new InstalledFontCollection();
            var names = new HashSet<string>();
            foreach (var f in col.Families)
            {
                names.Add(f.Name);
            }

            if (names.Contains("Segoe Fluent Icons"))
            {
                return "Segoe Fluent Icons";
            }

            if (names.Contains("Segoe MDL2 Assets"))
            {
                return "Segoe MDL2 Assets";
            }
        }
        catch
        {
            // Fall through to default; DrawIconGlyph will render a missing-glyph
            // box, which is still better than a blurry bitmap.
        }

        return "Segoe Fluent Icons";
    }

}
