using PeekDows.Core.Models;

namespace PeekDows.App.Settings;

internal sealed record ArrangePreviewZone(Rect Bounds, string[] Windows);

internal sealed class ArrangePreviewScreen : Form
{
    private readonly IReadOnlyList<ArrangePreviewZone> _zones;

    public ArrangePreviewScreen(Rect workArea, IReadOnlyList<ArrangePreviewZone> zones)
    {
        _zones = zones;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
        BackColor = Color.FromArgb(20, 31, 53);
        ForeColor = Color.White;
        Opacity = 0.80;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.LightSkyBlue, 2);
        using var background = new SolidBrush(Color.FromArgb(18, 43, 71));
        using var text = new SolidBrush(Color.White);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        int row = 0;
        foreach (var zone in _zones)
        {
            var rect = zone.Bounds;
            int x = rect.Left - Left, y = rect.Top - Top;
            e.Graphics.DrawRectangle(border, x + 2, y + 2, Math.Max(1, rect.Width - 5), Math.Max(1, rect.Height - 5));
            var label = zone.Windows.Length == 1 ? zone.Windows[0] :
                $"{zone.Windows.Length} windows · {string.Join(" / ", zone.Windows)}";
            var yOffset = _zones.Count > 1 && _zones.Any(other => other != zone &&
                other.Bounds.Left < rect.Right && other.Bounds.Right > rect.Left &&
                other.Bounds.Top < rect.Bottom && other.Bounds.Bottom > rect.Top)
                ? row++ * 24 : 0;
            var caption = new RectangleF(x + 7, y + 8 + yOffset, Math.Max(1, rect.Width - 15), 21);
            e.Graphics.FillRectangle(background, caption);
            e.Graphics.DrawString(label, Font, text, caption, format);
        }
    }
}

internal sealed class ArrangePreviewForm : Form
{
    public ArrangePreviewForm(int moving, IReadOnlyList<string> ignored, bool custom)
    {
        Text = "Preview Arrange";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 264);
        AutoScaleMode = AutoScaleMode.Dpi;
        var summary = new Label { AutoSize = true, Text = $"{moving} to arrange · {ignored.Count} left in place" };
        var list = new ListBox { Width = 400, Height = 108, AccessibleName = "Windows left in place" };
        list.Items.AddRange(ignored.ToArray());
        var apply = new Button { Text = "Arrange", DialogResult = DialogResult.OK, Enabled = moving > 0 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel { AutoSize = true };
        footer.Controls.AddRange([apply, cancel]);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12), WrapContents = false, AutoScroll = true };
        flow.Controls.AddRange([summary, list]);
        if (custom)
            flow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(392, 0),
                Text = "A window that cannot fit its custom zone can use the full work area." });
        flow.Controls.Add(footer);
        Controls.Add(flow);
        AcceptButton = apply;
        CancelButton = cancel;
        Shown += (_, _) => (apply.Enabled ? apply : cancel).Focus();
    }
}
