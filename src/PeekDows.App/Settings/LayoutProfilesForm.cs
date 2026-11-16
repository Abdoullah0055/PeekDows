using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

internal sealed class LayoutProfilesForm : Form
{
    private sealed record Target(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 395 };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _override = new() { Text = "Use a profile on this display", AutoSize = true };
    private readonly CheckBox _split = new() { Text = "Split the right zone horizontally", AutoSize = true };
    private readonly NumericUpDown _width = new() { Minimum = 20, Maximum = 80, Width = 62 };
    private readonly NumericUpDown _height = new() { Minimum = 20, Maximum = 80, Width = 62 };
    private readonly TextBox _rules = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 395, Height = 100 };
    private readonly Panel _zonePreview = new() { Width = 395, Height = 73, BackColor = Color.AliceBlue };
    private Target? _current;

    internal string DefaultMode { get; private set; }
    internal CustomZoneLayout DefaultZones { get; private set; }
    internal Dictionary<string, MonitorLayoutProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public LayoutProfilesForm(AppSettings settings)
    {
        Text = "PeekDows Layout Profiles";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(445, 492);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        DefaultMode = MonitorLayoutSettings.NormalizeMode(settings.LayoutMode);
        DefaultZones = settings.CustomZones ?? new();
        foreach (var (id, profile) in settings.MonitorProfiles ?? new())
            Profiles.TryAdd(id, profile);

        _mode.Items.AddRange(["Grid", "Halves", "Thirds", "Custom"]);
        _target.Items.Add(new Target("", "Default for all displays"));
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in Screen.AllScreens)
        {
            var id = MonitorService.GetProfileId(screen.DeviceName);
            if (!connected.Add(id)) continue;
            var label = screen.DeviceName.Replace(@"\\.\", "");
            _target.Items.Add(new Target(id, $"{label} · {screen.Bounds.Width} × {screen.Bounds.Height}" +
                (screen.Primary ? " · Primary" : "")));
        }
        foreach (var id in Profiles.Keys.Where(id => !connected.Contains(id)))
            _target.Items.Add(new Target(id, $"Disconnected · {id}"));

        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12), WrapContents = false, AutoScroll = true
        };
        flow.Controls.AddRange([LabelFor("Display"), _target, _override, Row("Layout", _mode),
            Row("Left zone width (%)", _width), _split, Row("Top-right zone height (%)", _height), _zonePreview,
            LabelFor("Screen rules · app.exe, ignore  or  app.exe, A–H"), _rules,
            Row("", save, cancel)]);
        Controls.Add(flow);
        AcceptButton = save;
        CancelButton = cancel;
        _target.SelectedIndexChanged += (_, _) => SelectTarget();
        _override.CheckedChanged += (_, _) => RefreshControls();
        _split.CheckedChanged += (_, _) => RefreshControls();
        _mode.SelectedIndexChanged += (_, _) => RefreshControls();
        _zonePreview.Paint += PaintZones;
        _width.ValueChanged += (_, _) => _zonePreview.Invalidate();
        _height.ValueChanged += (_, _) => _zonePreview.Invalidate();
        save.Click += (_, _) =>
        {
            if (StoreCurrent()) DialogResult = DialogResult.OK;
        };
        _target.SelectedIndex = 0;
    }

    private void SelectTarget()
    {
        if (_target.SelectedItem is not Target target || target == _current) return;
        if (!StoreCurrent())
        {
            _target.SelectedItem = _current;
            return;
        }
        _current = target;
        Profiles.TryGetValue(target.Key, out var profile);
        var zones = profile?.Zones ?? DefaultZones;
        _override.Checked = profile is not null;
        _mode.SelectedItem = MonitorLayoutSettings.NormalizeMode(profile?.Mode ?? DefaultMode);
        _width.Value = Math.Clamp(zones.WidthPercent, 20, 80);
        _height.Value = Math.Clamp(zones.HeightPercent, 20, 80);
        _split.Checked = zones.SplitSecondary;
        _rules.Lines = profile?.Rules?.Select(rule => $"{rule.Process}, " +
            (AppRuleService.IsIgnoreAction(rule.Action) ? "ignore" : rule.Slot)).ToArray() ?? [];
        RefreshControls();
    }

    private void RefreshControls()
    {
        bool isDefault = _current?.Key.Length == 0;
        bool editable = isDefault || _override.Checked;
        _override.Enabled = !isDefault;
        _mode.Enabled = editable;
        _width.Enabled = _split.Enabled = editable && _mode.SelectedItem as string == "Custom";
        _height.Enabled = _split.Enabled && _split.Checked;
        _zonePreview.Visible = _width.Enabled;
        _zonePreview.Invalidate();
        _rules.Enabled = !isDefault && editable;
    }

    private bool StoreCurrent()
    {
        if (_current is null) return true;
        var mode = MonitorLayoutSettings.NormalizeMode(_mode.SelectedItem as string);
        var zones = ReadZones();
        if (_current.Key.Length == 0)
        {
            DefaultMode = mode;
            DefaultZones = zones;
            return true;
        }
        if (!_override.Checked)
        {
            Profiles.Remove(_current.Key);
            return true;
        }

        var rules = new List<AppRule>();
        foreach (var line in _rules.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || !AppRuleService.IsValidRule(new AppRule
                { Process = fields[0], Action = "pin", Slot = "A" }) ||
                (!AppRuleService.IsIgnoreAction(fields[1]) && !AppRuleService.IsValidSlot(fields[1])))
            {
                MessageBox.Show(this, $"Invalid rule: {line}\nEnter app.exe, ignore or app.exe, A–H.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            bool ignore = AppRuleService.IsIgnoreAction(fields[1]);
            rules.Add(new AppRule { Process = fields[0], Action = ignore ? "ignore" : "pin", Slot = ignore ? "" : fields[1] });
        }
        Profiles[_current.Key] = new(mode, AppRuleService.CleanRules(rules), zones);
        return true;
    }

    private CustomZoneLayout ReadZones() => new((int)_width.Value, _split.Checked, (int)_height.Value);

    private void PaintZones(object? sender, PaintEventArgs args)
    {
        var bounds = new Rect(1, 1, _zonePreview.Width - 2, _zonePreview.Height - 2);
        var zones = PeekDows.Core.Services.LayoutEngine.CalculateCustomZones(bounds, ReadZones());
        using var border = new Pen(Color.SteelBlue);
        for (int index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            args.Graphics.DrawRectangle(border, zone.Left, zone.Top, zone.Width - 1, zone.Height - 1);
            TextRenderer.DrawText(args.Graphics, $"Zone {index + 1}", Font,
                new Rectangle(zone.Left, zone.Top, zone.Width, zone.Height), Color.MidnightBlue,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 5, 0, 0) };

    private static FlowLayoutPanel Row(string label, params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        if (label.Length > 0) row.Controls.Add(LabelFor(label));
        row.Controls.AddRange(controls);
        return row;
    }
}
