using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Focus;

/// <summary>
/// "Ghost HUD" hint overlay: a per-pixel-alpha layered window centred on the
/// gesture anchor showing ONLY populated slots as minimalist grey arrows
/// (white when active) over a whisper-subtle radial grey halo.
/// Primary path: composition happens off-screen into a 32bpp ARGB bitmap
/// pushed atomically via UpdateLayeredWindow from a canonical GDI blit
/// (GetDC(NULL) -> CreateCompatibleDC -> GetHbitmap -> SelectObject ->
/// UpdateLayeredWindow -> restore/delete), so there is never a one-frame
/// flash of unpainted background.
/// Fallback path: if UpdateLayeredWindow fails twice (e.g. no alpha
/// composition available), the form switches to color-keyed mode
/// (BackColor = TransparencyKey = magic 1,2,3) and paints the EXACT same
/// scene via OnPaint/GDI+ through PaintScene.
/// Never takes focus, never intercepts clicks
/// (WS_EX_LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW).
/// </summary>
public sealed class HintOverlayForm : Form
{
    public const string WindowClassName = "PeekDowsHintOverlay";

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;

    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private const int BaseBox = 200;
    private const int RevealMs = 140;
    private const int PopMs = 100;
    private const int FrameMs = 30;

    /// <summary>Consecutive ULW failures before engaging the keyed fallback.</summary>
    private const int UlwFailureThreshold = 2;

    /// <summary>Magic key color: never visible while layered composition works.</summary>
    private static readonly Color KeyedMagic = Color.FromArgb(1, 2, 3);

    // Arrow metrics in unscaled px (ArrowGeometry applies the DPI scale).
    private const float ArrowInner = 16f;
    private const float ArrowReach = 80f;
    private const float ArrowActiveBonus = 8f;
    private const float ArrowHeadLen = 11f;
    private const float ArrowHeadHalf = 5f;

    // Idle arrow grey, active white, halo peak. All monochrome by design.
    private static readonly Color IdleGrey = Color.FromArgb(200, 181, 181, 181);
    private static readonly Color ActiveWhite = Color.FromArgb(255, 255, 255, 255);
    private const int HaloPeakAlpha = 22;
    private const int HaloRadius = 90;

    private static readonly DirectionalFocusSlot[] SlotOrder = ArrowGeometry.SlotOrder;

    private IReadOnlyList<DirectionalFocusSlot> _populated =
        Array.Empty<DirectionalFocusSlot>();
    private DirectionalFocusSlot? _activeSlot;
    private DirectionalFocusSlot? _lastPaintedActive;
    private double _reveal; // 0..1 global fade-in
    private double _pop;    // 0..1 active-slot pop
    private readonly System.Windows.Forms.Timer _animator;
    private readonly Action<string>? _log;
    private int _ulwFailures;
    private bool _keyedFallback;
    private bool _firstPushReported;

    /// <summary>
    /// Last UpdateLayeredWindow blit status (result + Win32 error + slot count)
    /// for diagnostics when no logger was provided.
    /// </summary>
    public string LastBlitStatus { get; private set; } = "not-pushed";

    public HintOverlayForm(Action<string>? log = null)
    {
        _log = log;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ControlBox = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Text = WindowClassName;
        Size = new Size(BaseBox, BaseBox);
        _animator = new System.Windows.Forms.Timer { Interval = FrameMs };
        _animator.Tick += OnAnimTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>
    /// Shows arrows for populated slots only, centred on the gesture anchor
    /// (clamped to the working area). Restarts the fade-in when hidden.
    /// </summary>
    public void ShowAt(
        Point anchor,
        IEnumerable<DirectionalFocusSlot> populatedSlots,
        DirectionalFocusSlot? activeSlot)
    {
        _populated = populatedSlots?.ToArray() ?? Array.Empty<DirectionalFocusSlot>();
        if (_activeSlot != activeSlot)
        {
            _activeSlot = activeSlot;
            _pop = 0; // replay the pop on slot change
        }

        float scale = DeviceDpi / 96f;
        int box = (int)Math.Round(BaseBox * scale);
        if (Width != box)
            Size = new Size(box, box);

        var area = Screen.FromPoint(anchor).WorkingArea;
        int x = Math.Clamp(anchor.X - Width / 2, area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(anchor.Y - Height / 2, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(x, y);

        if (!IsHandleCreated)
            CreateHandle();
        if (!Visible)
        {
            _reveal = 0;
            Show();
        }
        if (_reveal < 1 || _pop < 1)
            _animator.Start();
        PushBitmap();
    }

    public void HideHint()
    {
        _animator.Stop();
        _reveal = 0;
        _pop = 0;
        _lastPaintedActive = null;
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_keyedFallback)
            return; // layered path composes off-screen; nothing to paint on screen
        PaintScene(e.Graphics, Math.Max(1, Width), Math.Max(1, Height), DeviceDpi / 96f);
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        _reveal = Math.Min(1, _reveal + (double)FrameMs / RevealMs);
        _pop = Math.Min(1, _pop + (double)FrameMs / PopMs);
        if (!Visible)
        {
            _animator.Stop();
            return;
        }
        PushBitmap();
        if (_reveal >= 1 && _pop >= 1)
            _animator.Stop();
    }

    private void PushBitmap()
    {
        if (_keyedFallback)
        {
            Invalidate();
            return;
        }

        int w = Math.Max(1, Width);
        int h = Math.Max(1, Height);
        float scale = DeviceDpi / 96f;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            PaintScene(g, w, h, scale);
        }

        // Canonical layered blit: screen DC -> memory DC -> HBITMAP selected in
        // -> UpdateLayeredWindow -> restore/delete. Every native handle is
        // released in the finally block; the ULW result is checked (the old
        // Graphics.GetHdc() path never composed the alpha and ignored the result).
        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            ReportBlit(false, Marshal.GetLastWin32Error());
            return;
        }

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBmp = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;
        bool ok = false;
        int err = 0;
        try
        {
            memDc = CreateCompatibleDC(hdcScreen);
            if (memDc == IntPtr.Zero)
            {
                err = Marshal.GetLastWin32Error();
            }
            else
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                hOld = SelectObject(memDc, hBmp);
                if (hOld == IntPtr.Zero)
                {
                    err = Marshal.GetLastWin32Error();
                }
                else
                {
                    var topLeft = new POINT { X = Location.X, Y = Location.Y };
                    var size = new SIZE { Cx = w, Cy = h };
                    var src = new POINT { X = 0, Y = 0 };
                    var blend = new BLENDFUNCTION
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = (byte)Math.Round(255 * EaseOut(_reveal)),
                        AlphaFormat = AC_SRC_ALPHA,
                    };
                    ok = UpdateLayeredWindow(Handle, hdcScreen, ref topLeft, ref size,
                        memDc, ref src, 0, ref blend, ULW_ALPHA);
                    err = Marshal.GetLastWin32Error();
                }
            }
        }
        catch (Exception ex)
        {
            ok = false;
            err = ex.HResult;
        }
        finally
        {
            if (hOld != IntPtr.Zero && memDc != IntPtr.Zero)
                SelectObject(memDc, hOld);
            if (hBmp != IntPtr.Zero)
                DeleteObject(hBmp);
            if (memDc != IntPtr.Zero)
                DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }

        ReportBlit(ok, err);
        _lastPaintedActive = _activeSlot;
    }

    private void ReportBlit(bool ok, int win32Error)
    {
        LastBlitStatus = $"ULW ok={ok} win32={win32Error} populated={_populated.Count}";
        if (!_firstPushReported || !ok)
            _log?.Invoke(LastBlitStatus);
        _firstPushReported = true;
        if (ok)
            return;
        _ulwFailures++;
        if (_ulwFailures >= UlwFailureThreshold && !_keyedFallback)
            EnterKeyedFallback();
    }

    private void EnterKeyedFallback()
    {
        _keyedFallback = true;
        BackColor = KeyedMagic;
        TransparencyKey = KeyedMagic;
        if (IsHandleCreated && !Visible)
            Show();
        Invalidate();
        _log?.Invoke("HintOverlay keyed fallback engaged: " + LastBlitStatus);
    }

    /// <summary>
    /// Single scene painter shared by the layered bitmap path and the keyed
    /// OnPaint fallback, guaranteeing identical visuals on both paths.
    /// </summary>
    private void PaintScene(Graphics g, int w, int h, float scale)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintHalo(g, w, h, scale);
        PaintArrows(g, w / 2f, scale);
    }

    private void PaintHalo(Graphics g, int w, int h, float scale)
    {
        float radius = HaloRadius * scale;
        var center = new PointF(w / 2f, h / 2f);
        using var path = new GraphicsPath();
        path.AddEllipse(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(HaloPeakAlpha, 180, 180, 180),
            SurroundColors = [Color.FromArgb(0, 180, 180, 180)],
        };
        g.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
    }

    private void PaintArrows(Graphics g, float center, float scale)
    {
        var set = new HashSet<DirectionalFocusSlot>(_populated);
        var ordered = SlotOrder.Where(set.Contains).ToArray();
        if (ordered.Length == 0)
            return;

        // Active arrow pops 80->88px; idle arrows rest at full length immediately.
        float bonus = ArrowActiveBonus * (float)EaseOut(_pop);
        var geoms = ArrowGeometry.ComputeArrows(
            ordered, _activeSlot, center,
            ArrowInner, ArrowReach, bonus, ArrowHeadLen, ArrowHeadHalf, scale);

        for (int i = 0; i < ordered.Length; i++)
        {
            bool isActive = _activeSlot == ordered[i];
            float penWidth = (isActive ? 4f : 3f) * scale;

            var (dx, dy) = ArrowGeometry.Direction(ordered[i]);
            var p1 = new PointF(geoms[i].P1.X, geoms[i].P1.Y);
            var tip = new PointF(geoms[i].Tip.X, geoms[i].Tip.Y);
            // Shaft stops where it always did: 2px (scaled) short of the tip,
            // covered by the head triangle.
            var shaftEnd = new PointF(tip.X - dx * 2f * scale, tip.Y - dy * 2f * scale);
            var head = new PointF[geoms[i].Head.Length];
            for (int k = 0; k < head.Length; k++)
                head[k] = new PointF(geoms[i].Head[k].X, geoms[i].Head[k].Y);

            if (isActive)
            {
                // Soft glow underlay + crisp white core.
                using var glow = new Pen(Color.FromArgb(60, 255, 255, 255), penWidth + 4f * scale)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };
                g.DrawLine(glow, p1, shaftEnd);
                using var pen = new Pen(ActiveWhite, penWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };
                using var brush = new SolidBrush(ActiveWhite);
                g.DrawLine(pen, p1, shaftEnd);
                g.FillPolygon(brush, head);
            }
            else
            {
                using var pen = new Pen(IdleGrey, penWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };
                using var brush = new SolidBrush(IdleGrey);
                g.DrawLine(pen, p1, shaftEnd);
                g.FillPolygon(brush, head);
            }
        }
    }

    private static double EaseOut(double t) => 1 - (1 - t) * (1 - t);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animator.Tick -= OnAnimTick;
            _animator.Stop();
            _animator.Dispose();
            _populated = Array.Empty<DirectionalFocusSlot>();
            _activeSlot = null;
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);
}
