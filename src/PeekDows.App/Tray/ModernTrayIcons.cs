using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PeekDows.App.Tray;

/// <summary>
/// Generates the tray menu's monochrome icons procedurally with GDI+.
/// Drawing vectorially (instead of shipping .png assets) keeps the icons crisp
/// at any DPI and lets the status indicator change color at runtime.
/// </summary>
internal static class ModernTrayIcons
{
    /// <summary>
    /// Logical icon size in pixels at 96 DPI. The actual bitmap is scaled by the
    /// requested pixel size, so high-DPI displays get a sharper image.
    /// </summary>
    private const float LogicalSize = 16f;

    /// <summary>
    /// Standard icon set, drawn in the foreground text color. Cached for the
    /// lifetime of the process; the menu keeps references to these bitmaps.
    /// </summary>
    private static readonly Dictionary<string, Bitmap> _cache = new();

    public static Bitmap Get(string key, int pixelSize = 16)
    {
        string cacheKey = $"{key}:{pixelSize}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var bmp = new Bitmap(pixelSize, pixelSize);
        bmp.SetResolution(pixelSize, pixelSize);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawIcon(g, key, pixelSize, ModernTrayPalette.Text);
        }
        _cache[cacheKey] = bmp;
        return bmp;
    }

    /// <summary>
    /// Status dot — drawn on demand because its color depends on the runtime state.
    /// Never cached (color varies), so callers must Dispose the previous image when
    /// swapping.
    /// </summary>
    public static Bitmap StatusDot(int pixelSize, Color color)
    {
        var bmp = new Bitmap(pixelSize, pixelSize);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = pixelSize * 0.5f;
            var rect = new RectangleF((pixelSize - d) / 2f, (pixelSize - d) / 2f, d, d);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, rect);
        }
        return bmp;
    }

    private static void DrawIcon(Graphics g, string key, int size, Color color)
    {
        // 16x16 logical coordinate system, independent of bitmap pixel size.
        g.ScaleTransform(size / LogicalSize, size / LogicalSize);
        using var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);

        switch (key)
        {
            case "arrange": DrawArrange(g, pen); break;
            case "pause": DrawPause(g, pen); break;
            case "pause-for": DrawPauseFor(g, pen); break;
            case "auto-arrange": DrawAutoArrange(g, pen); break;
            case "reposition-maximized": DrawRepositionMaximized(g, pen); break;
            case "start-windows": DrawStartWindows(g, pen, brush); break;
            case "directional-focus": DrawDirectionalFocus(g, pen); break;
            case "settings": DrawSettings(g, pen); break;
            case "log-file": DrawLogFile(g, pen); break;
            case "logs-folder": DrawLogsFolder(g, pen); break;
            case "exit": DrawExit(g, pen); break;
            default: break;
        }
    }

    // Arrange Now: four overlapping window tiles forming a peek stack.
    private static void DrawArrange(Graphics g, Pen pen)
    {
        var back = new RectangleF(2.5f, 4.5f, 8f, 8f);
        var front = new RectangleF(5.5f, 2.5f, 8f, 8f);
        g.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height);
        g.DrawRectangle(pen, front.X, front.Y, front.Width, front.Height);
    }

    // Pause: two vertical bars.
    private static void DrawPause(Graphics g, Pen pen)
    {
        g.FillRectangle(pen.Brush, 4.5f, 3.5f, 2f, 9f);
        g.FillRectangle(pen.Brush, 9.5f, 3.5f, 2f, 9f);
    }

    // Pause for: pause bars + a small clock arc.
    private static void DrawPauseFor(Graphics g, Pen pen)
    {
        g.FillRectangle(pen.Brush, 3.5f, 3.5f, 2f, 6f);
        g.FillRectangle(pen.Brush, 7f, 3.5f, 2f, 6f);
        g.DrawEllipse(pen, 8.5f, 8.5f, 5f, 5f);
        g.DrawLine(pen, 11f, 10f, 11f, 9f);
        g.DrawLine(pen, 11f, 11f, 12.2f, 11f);
    }

    // Auto Arrange: a circular "cycle" arrow.
    private static void DrawAutoArrange(Graphics g, Pen pen)
    {
        g.DrawArc(pen, 2.5f, 2.5f, 11f, 11f, -40f, 270f);
        // arrowhead
        var pts = new PointF[]
        {
            new(12.2f, 3.2f),
            new(9.8f, 3.0f),
            new(11.2f, 5.2f),
        };
        g.FillPolygon(pen.Brush, pts);
    }

    // Reposition maximized: a window with an outward expand arrow.
    private static void DrawRepositionMaximized(Graphics g, Pen pen)
    {
        g.DrawRectangle(pen, 2.5f, 4f, 11f, 8.5f);
        g.DrawLine(pen, 2.5f, 6.5f, 13.5f, 6.5f);
        // expand arrows in corners
        g.DrawLine(pen, 4f, 9f, 4f, 11f);
        g.DrawLine(pen, 4f, 11f, 6f, 11f);
        g.DrawLine(pen, 12f, 9f, 12f, 11f);
        g.DrawLine(pen, 12f, 11f, 10f, 11f);
    }

    // Start with Windows: simplified 4-pane window logo.
    private static void DrawStartWindows(Graphics g, Pen pen, Brush brush)
    {
        float s = 4.5f;
        float gap = 0.8f;
        float x0 = (LogicalSize - 2 * s - gap) / 2f;
        float y0 = (LogicalSize - 2 * s - gap) / 2f;
        g.FillRectangle(brush, x0, y0, s, s);
        g.FillRectangle(brush, x0 + s + gap, y0, s, s);
        g.FillRectangle(brush, x0, y0 + s + gap, s, s);
        g.FillRectangle(brush, x0 + s + gap, y0 + s + gap, s, s);
    }

    // Directional Focus: four directional arrows from a center point.
    private static void DrawDirectionalFocus(Graphics g, Pen pen)
    {
        float c = LogicalSize / 2f;
        // up / down / left / right
        g.DrawLine(pen, c, 2.5f, c, 6f);
        g.DrawLine(pen, c - 1.4f, 4f, c, 2.5f);
        g.DrawLine(pen, c + 1.4f, 4f, c, 2.5f);

        g.DrawLine(pen, c, 10f, c, 13.5f);
        g.DrawLine(pen, c - 1.4f, 12f, c, 13.5f);
        g.DrawLine(pen, c + 1.4f, 12f, c, 13.5f);

        g.DrawLine(pen, 2.5f, c, 6f, c);
        g.DrawLine(pen, 4f, c - 1.4f, 2.5f, c);
        g.DrawLine(pen, 4f, c + 1.4f, 2.5f, c);

        g.DrawLine(pen, 10f, c, 13.5f, c);
        g.DrawLine(pen, 12f, c - 1.4f, 13.5f, c);
        g.DrawLine(pen, 12f, c + 1.4f, 13.5f, c);

        g.FillEllipse(pen.Brush, c - 1.1f, c - 1.1f, 2.2f, 2.2f);
    }

    // Settings: a gear (simplified).
    private static void DrawSettings(Graphics g, Pen pen)
    {
        float c = LogicalSize / 2f;
        g.DrawEllipse(pen, 4.5f, 4.5f, 7f, 7f);
        g.DrawEllipse(pen, c - 1f, c - 1f, 2f, 2f);
        // 8 teeth
        for (int i = 0; i < 8; i++)
        {
            float angle = i * (360f / 8f);
            float rad = angle * System.MathF.PI / 180f;
            float x1 = c + System.MathF.Cos(rad) * 4.5f;
            float y1 = c + System.MathF.Sin(rad) * 4.5f;
            float x2 = c + System.MathF.Cos(rad) * 6f;
            float y2 = c + System.MathF.Sin(rad) * 6f;
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    // Log file: a document with folded corner and text lines.
    private static void DrawLogFile(Graphics g, Pen pen)
    {
        var path = new GraphicsPath();
        path.AddLines(new PointF[]
        {
            new(4f, 2.5f),
            new(9.5f, 2.5f),
            new(12f, 5f),
            new(12f, 13.5f),
            new(4f, 13.5f),
        });
        path.CloseFigure();
        g.DrawPath(pen, path);
        g.DrawLine(pen, 9.5f, 2.5f, 9.5f, 5f);
        g.DrawLine(pen, 9.5f, 5f, 12f, 5f);
        g.DrawLine(pen, 6f, 8f, 10f, 8f);
        g.DrawLine(pen, 6f, 10.5f, 10f, 10.5f);
    }

    // Logs folder: a classic folder shape.
    private static void DrawLogsFolder(Graphics g, Pen pen)
    {
        var path = new GraphicsPath();
        path.AddLines(new PointF[]
        {
            new(2.5f, 5.5f),
            new(6f, 5.5f),
            new(7.5f, 4f),
            new(13.5f, 4f),
            new(13.5f, 12.5f),
            new(2.5f, 12.5f),
        });
        path.CloseFigure();
        g.DrawPath(pen, path);
    }

    // Exit: a power symbol.
    private static void DrawExit(Graphics g, Pen pen)
    {
        float c = LogicalSize / 2f;
        g.DrawArc(pen, 4f, 4f, 8f, 8f, 45f, 270f);
        g.DrawLine(pen, c, 2.5f, c, 7.5f);
    }
}
