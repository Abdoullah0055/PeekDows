using System;
using System.Linq;
using System.Windows.Forms;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>
/// Legacy minimal settings window (ignore lists only). Kept as a fallback for machines
/// where the WebView2 runtime is unavailable. The modern UI is SettingsHostForm.
/// </summary>
public class SettingsLegacyWindow : Form
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private TextBox _ignoredProcessesBox;
    private TextBox _ignoredClassesBox;

    public event Action? SettingsSaved;

    public SettingsLegacyWindow(AppSettings settings, SettingsService settingsService)
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

        var y = 12;

        var processesLabel = new Label
        {
            Text = "Ignored Processes (one per line):",
            Location = new System.Drawing.Point(12, y),
            AutoSize = true
        };
        Controls.Add(processesLabel);
        y += 22;

        _ignoredProcessesBox = new TextBox
        {
            Location = new System.Drawing.Point(12, y),
            Width = 480,
            Height = 120,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _settings.IgnoredProcesses)
        };
        Controls.Add(_ignoredProcessesBox);
        y += 128;

        var classesLabel = new Label
        {
            Text = "Ignored Window Classes (one per line):",
            Location = new System.Drawing.Point(12, y),
            AutoSize = true
        };
        Controls.Add(classesLabel);
        y += 22;

        _ignoredClassesBox = new TextBox
        {
            Location = new System.Drawing.Point(12, y),
            Width = 480,
            Height = 120,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _settings.IgnoredClasses)
        };
        Controls.Add(_ignoredClassesBox);
        y += 128;

        var saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(12, y),
            Width = 100,
            Height = 30
        };
        saveButton.Click += OnSave;
        Controls.Add(saveButton);

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(120, y),
            Width = 100,
            Height = 30
        };
        cancelButton.Click += (s, e) => Close();
        Controls.Add(cancelButton);
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
