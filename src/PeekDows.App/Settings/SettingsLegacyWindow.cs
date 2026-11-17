using System;
using System.Linq;
using System.Windows.Forms;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>
/// Basic settings window. Kept as a fallback for machines
/// where the WebView2 runtime is unavailable. The modern UI is SettingsHostForm.
/// </summary>
public class SettingsLegacyWindow : Form
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private TextBox _ignoredProcessesBox;
    private TextBox _ignoredClassesBox;

    public event Action? SettingsSaved;

    public SettingsLegacyWindow(AppSettings settings, SettingsService settingsService, IPeekDowsController? controller = null)
    {
        _settings = settings;
        _settingsService = settingsService;

        Text = "PeekDows Settings";
        Width = 520;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var ignored = new TabPage("Ignored apps");
        Controls.Add(tabs);
        if (controller != null)
            AddFallbackSettings(tabs, controller);
        tabs.TabPages.Add(ignored);
        var y = 12;

        var processesLabel = new Label
        {
            Text = "Ignored Processes (one per line):",
            Location = new System.Drawing.Point(12, y),
            AutoSize = true
        };
        ignored.Controls.Add(processesLabel);
        y += 22;

        _ignoredProcessesBox = new TextBox
        {
            Location = new System.Drawing.Point(12, y),
            Width = 460,
            Height = 120,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _settings.IgnoredProcesses)
        };
        ignored.Controls.Add(_ignoredProcessesBox);
        y += 128;

        var classesLabel = new Label
        {
            Text = "Ignored Window Classes (one per line):",
            Location = new System.Drawing.Point(12, y),
            AutoSize = true
        };
        ignored.Controls.Add(classesLabel);
        y += 22;

        _ignoredClassesBox = new TextBox
        {
            Location = new System.Drawing.Point(12, y),
            Width = 460,
            Height = 120,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _settings.IgnoredClasses)
        };
        ignored.Controls.Add(_ignoredClassesBox);
        y += 128;

        var saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(12, y),
            Width = 100,
            Height = 30
        };
        saveButton.Click += OnSave;
        ignored.Controls.Add(saveButton);

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(120, y),
            Width = 100,
            Height = 30
        };
        cancelButton.Click += (s, e) => Close();
        ignored.Controls.Add(cancelButton);
    }

    private void AddFallbackSettings(TabControl tabs, IPeekDowsController controller)
    {
        FlowLayoutPanel AddPage(string name)
        {
            var tab = new TabPage(name);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            tab.Controls.Add(panel);
            tabs.TabPages.Add(tab);
            return panel;
        }

        static void AddButton(Control parent, string label, Action action)
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (_, _) => action();
            parent.Controls.Add(button);
        }

        void AddToggle(Control parent, string label, Func<bool> read, Action toggle)
        {
            var box = new CheckBox { Text = label, AutoSize = true, Checked = read() };
            box.Click += (_, _) =>
            {
                if (box.Checked != read()) toggle();
                box.Checked = read();
            };
            Activated += (_, _) => box.Checked = read();
            parent.Controls.Add(box);
        }

        void AddSelection(Control parent, string label, string[] choices, Func<int> read, Action<int> save)
        {
            var caption = new Label { Text = label, AutoSize = true };
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = label, Width = 220 };
            combo.Items.AddRange(choices);
            combo.SelectedIndex = read();
            combo.SelectionChangeCommitted += (_, _) => { save(combo.SelectedIndex); combo.SelectedIndex = read(); };
            Activated += (_, _) => combo.SelectedIndex = read();
            parent.Controls.Add(caption);
            parent.Controls.Add(combo);
        }

        var general = AddPage("General");
        var status = new Label { AutoSize = true };
        var pause = new FlowLayoutPanel { AutoSize = true };
        var duration = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Pause duration" };
        duration.Items.AddRange(["5 minutes", "15 minutes", "1 hour", "Until manually resumed"]);
        duration.SelectedIndex = 0;
        pause.Controls.Add(duration);
        AddButton(pause, "Pause", () =>
        {
            if (duration.SelectedIndex == 3) controller.PauseUntilResumed();
            else controller.PauseFor(TimeSpan.FromMinutes(duration.SelectedIndex switch { 0 => 5, 1 => 15, _ => 60 }));
        });
        AddButton(pause, "Resume", controller.Resume);
        general.Controls.Add(status);
        general.Controls.Add(pause);
        void UpdateStatus(RuntimeState state)
        {
            status.Text = state == RuntimeState.Paused ? controller.PauseDescription ?? "Paused" : "Running";
            pause.Controls[2].Enabled = state == RuntimeState.Paused;
        }
        controller.StateChanged += UpdateStatus;
        Disposed += (_, _) => controller.StateChanged -= UpdateStatus;
        UpdateStatus(controller.State);
        AddToggle(general, "Auto arrange", () => _settings.AutoArrange, controller.ToggleAutoArrange);
        AddToggle(general, "Animate window transitions", () => _settings.AnimateWindowTransitions, controller.ToggleAnimateWindowTransitions);
        AddToggle(general, "Directional focus", () => _settings.DirectionalFocusEnabled, controller.ToggleDirectionalFocus);
        AddToggle(general, "Start with Windows", () => _settings.StartWithWindows, controller.ToggleStartWithWindows);
        AddToggle(general, "Arrange sound", () => _settings.ArrangeSoundEnabled, controller.ToggleArrangeSound);
        AddSelection(general, "Focus hints", ["Off", "Overlay"], () => _settings.FocusHintMode == "Off" ? 0 : 1,
            index => controller.SetFocusHintMode(index == 0 ? "Off" : "Overlay"));

        var layout = AddPage("Layout");
        string[] modes = ["Grid", "Halves", "Thirds", "Custom"];
        AddSelection(layout, "Layout mode", modes, () => Math.Max(0, Array.IndexOf(modes, _settings.LayoutMode)),
            index => controller.SetLayoutMode(modes[index]));
        WindowSizePreset[] presets = [WindowSizePreset.Small, WindowSizePreset.Medium, WindowSizePreset.Large];
        AddSelection(layout, "Window size preset", ["Small · 90%", "Medium · 95%", "Large · 98%"],
            () => Math.Max(0, Array.IndexOf(presets, _settings.WindowSizePreset)), index => controller.SetWindowSizePreset(presets[index]));
        AddToggle(layout, "Reposition maximized windows", () => _settings.AllowRepositionMaximizedWindows,
            controller.ToggleAllowRepositionMaximizedWindows);
        AddButton(layout, "Edit profiles", controller.OpenLayoutProfiles);
        AddButton(layout, "Export layout settings", controller.ExportLayoutSettings);
        AddButton(layout, "Import layout settings", controller.ImportLayoutSettings);
        AddButton(layout, "Preview arrange", controller.PreviewArrange);
        AddButton(layout, "Next window in zone", controller.CycleZoneWindow);

        var about = AddPage("About");
        AddButton(about, "Open log file", controller.OpenLogFile);
        AddButton(about, "Open logs folder", controller.OpenLogsFolder);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var processes = _ignoredProcessesBox.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var classes = _ignoredClassesBox.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.IgnoredProcesses.Clear();
        _settings.IgnoredProcesses.AddRange(processes);

        _settings.IgnoredClasses.Clear();
        _settings.IgnoredClasses.AddRange(classes);

        _settingsService.Save(_settings);
        SettingsSaved?.Invoke();

        MessageBox.Show("Settings saved.", "PeekDows", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }
}
