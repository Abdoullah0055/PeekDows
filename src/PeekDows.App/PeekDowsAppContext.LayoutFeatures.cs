using PeekDows.App.Settings;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App;

public partial class PeekDowsAppContext
{
    private sealed record ZoneWindow(IntPtr Handle, int Process, string Title);
    private sealed record StackedZone(Rect Area, ZoneWindow[] Windows);
    private StackedZone[] _stackedZones = [];

    public void PreviewArrange()
    {
        if (!IsPaused && _settings.Enabled)
            ExecuteArrange(preview: true);
    }

    private bool ShowArrangePreview(IReadOnlyList<WindowPlacement> placements,
        IReadOnlyList<ManagedWindow> eligible, IReadOnlyList<RawWindowInfo> discovered)
    {
        var names = eligible.ToDictionary(window => window.Hwnd, window => window.Title);
        var targets = placements.Select(placement => placement.Hwnd).ToHashSet();
        var ignored = discovered.Where(window => !targets.Contains(window.Hwnd) && window.IsVisible && !window.IsCloaked &&
                !window.IsMinimized && window.ProcessId != Environment.ProcessId &&
                window.CurrentRect.Width >= 250 && window.CurrentRect.Height >= 180 &&
                !string.IsNullOrWhiteSpace(window.Title) && !_classifier.IsSystemWindow(window) &&
                _virtualDesktopService.IsWindowOnCurrentVirtualDesktop(window.Hwnd))
            .Select(window => window.Title).ToArray();
        var overlays = placements.GroupBy(placement => _monitorService.GetMonitorForWindow(placement.Hwnd).WorkArea)
            .Select(screen => new ArrangePreviewScreen(screen.Key, screen.GroupBy(placement => placement.TargetRect)
                .Select(zone => new ArrangePreviewZone(zone.Key, zone.Select(placement =>
                    names.GetValueOrDefault(placement.Hwnd, "Window")).ToArray())).ToArray())).ToArray();
        try
        {
            foreach (var overlay in overlays) overlay.Show();
            using var dialog = new ArrangePreviewForm(placements.Count, ignored, placements.Any(p => IsCustomSlot(p.SlotId)));
            return dialog.ShowDialog() == DialogResult.OK;
        }
        finally
        {
            foreach (var overlay in overlays) overlay.Dispose();
        }
    }

    private (string Layout, string Screens, bool RepositionMaximized, int Generation,
        WindowSizePreset Preset, string IgnoredProcesses, string IgnoredClasses) ReadPreviewStamp() =>
        (LayoutPreferencesFile.Capture(_settings).ToJson(),
            string.Join("|", Screen.AllScreens.Select(s => $"{s.DeviceName}:{s.WorkingArea}").Order()),
            _settings.AllowRepositionMaximizedWindows, _placementGeneration, _settings.WindowSizePreset,
            string.Join("|", _settings.IgnoredProcesses), string.Join("|", _settings.IgnoredClasses));

    private bool StillMatchesPreview(IReadOnlyList<WindowPlacement> placements, IReadOnlyList<ManagedWindow> windows,
        IReadOnlyDictionary<IntPtr, MonitorInfo> monitors)
    {
        var candidates = windows.ToDictionary(window => window.Hwnd);
        var live = _discoveryService.GetTopLevelWindows().Where(_classifier.IsEligible).ToDictionary(window => window.Hwnd);
        if (!candidates.Keys.ToHashSet().SetEquals(live.Keys.Where(hwnd =>
            !live[hwnd].IsMaximized || _settings.AllowRepositionMaximizedWindows ||
            _loneWindowMaximizePolicy.WasMaximizedByPeekDows(hwnd))))
            return false;
        foreach (var placement in placements)
        {
            var old = candidates[placement.Hwnd];
            if (!live.TryGetValue(placement.Hwnd, out var current) ||
                old.ProcessId != current.ProcessId || old.IsMaximized != current.IsMaximized ||
                old.CurrentRect != current.CurrentRect ||
                _monitorService.GetMonitorForWindow(old.Hwnd).Handle != monitors[old.Hwnd].Handle)
                return false;
        }
        return true;
    }

    private void UpdateStackedZones(IReadOnlyList<WindowPlacement> placements, IReadOnlyList<ManagedWindow> windows,
        Func<IntPtr, MonitorInfo> resolveMonitor)
    {
        var known = windows.ToDictionary(window => window.Hwnd);
        _stackedZones = placements.Where(p => p.SlotId is "Left" or "Right" or "Col1" or "Col2" or "Col3")
            .GroupBy(placement => (resolveMonitor(placement.Hwnd).Handle, placement.TargetRect))
            .Where(group => group.Count() > 1)
            .Select(group => new StackedZone(group.Key.TargetRect, group.Select(placement =>
                new ZoneWindow(placement.Hwnd, known[placement.Hwnd].ProcessId,
                    known[placement.Hwnd].Title)).ToArray())).ToArray();
    }

    public void CycleZoneWindow()
    {
        // Settings owns focus and the pointer when this runs. Ask which stack
        // to use when there are several, then find its topmost app window.
        var choices = _stackedZones.Select(zone => zone with
            { Windows = zone.Windows.Where(window => IsStillInZone(window, zone.Area)).ToArray() })
            .Where(zone => zone.Windows.Length > 1).ToArray();
        if (choices.Length == 0)
        {
            MessageBox.Show("Arrange at least two windows in the same Halves or Thirds zone first.",
                "Next window in zone", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var zone = choices.Length == 1 ? choices[0] : SelectStackedZone(choices);
        if (zone is null) return;

        var available = zone.Windows.Where(window => IsStillInZone(window, zone.Area)).ToArray();
        if (available.Length < 2) return;
        var handles = available.Select(window => window.Handle).ToHashSet();
        var topmost = _discoveryService.GetTopLevelWindows().FirstOrDefault(window => handles.Contains(window.Hwnd));
        var index = topmost is null ? -1 : Array.FindIndex(available, window => window.Handle == topmost.Hwnd);
        _windowActivationService.Activate(available[(index + 1) % available.Length].Handle);
    }

    private bool IsStillInZone(ZoneWindow window, Rect area)
    {
        if (!NativeMethods.IsWindowVisible(window.Handle) || NativeMethods.IsIconic(window.Handle) ||
            !_virtualDesktopService.IsWindowOnCurrentVirtualDesktop(window.Handle) ||
            !NativeMethods.GetWindowRect(window.Handle, out var rect))
            return false;
        NativeMethods.GetWindowThreadProcessId(window.Handle, out uint pid);
        var x = rect.left + (rect.right - rect.left) / 2;
        var y = rect.top + (rect.bottom - rect.top) / 2;
        return pid == (uint)window.Process && area.Left <= x && x < area.Right &&
            area.Top <= y && y < area.Bottom;
    }

    private static StackedZone? SelectStackedZone(StackedZone[] choices)
    {
        using var dialog = new Form { Text = "Next window in zone", Width = 500, Height = 250,
            ShowInTaskbar = false, StartPosition = FormStartPosition.CenterScreen };
        var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Stacked zones" };
        foreach (var zone in choices)
            list.Items.Add($"{zone.Windows.Length} windows · {string.Join(" / ", zone.Windows.Select(window => window.Title))}");
        list.SelectedIndex = 0;
        var next = new Button { Text = "Activate next", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Dock = DockStyle.Bottom, DialogResult = DialogResult.Cancel };
        dialog.Controls.Add(list);
        dialog.Controls.Add(next);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = next;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog() == DialogResult.OK && list.SelectedIndex >= 0
            ? choices.ElementAtOrDefault(list.SelectedIndex) : null;
    }

    public void OpenLayoutProfiles()
    {
        using var dialog = new LayoutProfilesForm(_settings);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var old = LayoutPreferencesFile.Capture(_settings);
        try
        {
            SetLayoutPreferences(old with { LayoutMode = dialog.DefaultMode,
                CustomZones = dialog.DefaultZones, MonitorProfiles = dialog.Profiles });
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            SetLayoutPreferences(old);
            ShowLayoutError(ex);
            return;
        }
        LayoutPreferencesChanged();
    }

    public void ExportLayoutSettings()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export PeekDows layout settings", Filter = "JSON (*.json)|*.json",
            DefaultExt = "json", FileName = "peekdows-layout.json"
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, LayoutPreferencesFile.Capture(_settings).ToJson()); }
        catch (Exception ex) { ShowLayoutError(ex); }
    }

    public void ImportLayoutSettings()
    {
        using var dialog = new OpenFileDialog { Title = "Import PeekDows layout settings", Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var incoming = LayoutPreferencesFile.Read(dialog.FileName);
            var old = LayoutPreferencesFile.Capture(_settings);
            var nextHotkeys = DefaultHotkeys.ToDictionary(pair => pair.Key,
                pair => incoming.Hotkeys.GetValueOrDefault(pair.Key, pair.Value));
            if (nextHotkeys.Values.Any(gesture => !HotkeyParser.TryParse(gesture, out _, out _)))
                throw new InvalidDataException("The file contains an invalid shortcut.");

            using var confirmation = new Form { Text = "Confirm layout settings import", Width = 720, Height = 470,
                StartPosition = FormStartPosition.CenterScreen, MinimizeBox = false, ShowInTaskbar = false };
            var detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Text = DescribeImport(old, incoming, nextHotkeys), WordWrap = true };
            var apply = new Button { Text = "Import", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            actions.Controls.AddRange([cancel, apply]);
            confirmation.Controls.Add(detail);
            confirmation.Controls.Add(actions);
            confirmation.AcceptButton = cancel;
            confirmation.CancelButton = cancel;
            if (confirmation.ShowDialog() != DialogResult.OK) return;
            if (LayoutPreferencesFile.Capture(_settings).ToJson() != old.ToJson())
                throw new InvalidOperationException("Settings changed while the import was open. Review the file again.");

            var backupPath = Path.Combine(Path.GetDirectoryName(_settingsService.SettingsFilePath)!,
                $"settings.before-import.{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
            new SettingsService(backupPath).Save(_settings);
            var changedHotkeys = nextHotkeys.Where(pair => !HotkeyParser.AreEquivalent(pair.Value,
                old.Hotkeys.GetValueOrDefault(pair.Key, DefaultHotkeys[pair.Key]))).ToDictionary();
            if (!_hotkeyService.TryUpdateGestures(changedHotkeys, out var error, SaveImportedSettings))
                throw new InvalidOperationException($"Could not register imported shortcuts ({error}).");
            LayoutPreferencesChanged();

            void SaveImportedSettings()
            {
                try
                {
                    SetLayoutPreferences(incoming);
                    _settings.Hotkeys = new Dictionary<string, string>(incoming.Hotkeys);
                    foreach (var (name, gesture) in nextHotkeys) _settings.Hotkeys[name] = gesture;
                    _settingsService.Save(_settings);
                }
                catch
                {
                    SetLayoutPreferences(old);
                    _settings.Hotkeys = old.Hotkeys;
                    throw;
                }
            }
        }
        catch (Exception ex) { ShowLayoutError(ex); }
    }

    private static string DescribeImport(LayoutPreferencesFile old, LayoutPreferencesFile incoming,
        IReadOnlyDictionary<string, string> nextHotkeys)
    {
        static string Rules(IEnumerable<AppRule> rules) => string.Join(", ", rules.Select(rule =>
            $"{rule.Process} ({(rule.Action == "ignore" ? "ignore" : rule.Slot)})").DefaultIfEmpty("none"));
        static string Zones(CustomZoneLayout zones) => zones.SplitSecondary
            ? $"left {zones.WidthPercent}%, top-right {zones.HeightPercent}%"
            : $"left {zones.WidthPercent}%, right {100 - zones.WidthPercent}%";
        static string Profile(MonitorLayoutProfile? profile) => profile is null ? "default" :
            $"{profile.Mode}, {Rules(profile.Rules)}" + (profile.Zones is null ? "" : $", {Zones(profile.Zones)}");
        var lines = new List<string>
        {
            $"Default layout: {old.LayoutMode} → {incoming.LayoutMode}",
            $"Grid size: {old.Preset} → {incoming.Preset}",
            $"Custom zones: {Zones(old.CustomZones)} → {Zones(incoming.CustomZones)}",
            $"Default rules: {Rules(old.AppRules)} → {Rules(incoming.AppRules)}",
        };
        foreach (var key in old.MonitorProfiles.Keys.Union(incoming.MonitorProfiles.Keys, StringComparer.OrdinalIgnoreCase))
            lines.Add($"{key}: {Profile(old.MonitorProfiles.GetValueOrDefault(key))} → {Profile(incoming.MonitorProfiles.GetValueOrDefault(key))}");
        foreach (var (name, gesture) in nextHotkeys)
            lines.Add($"{name}: {old.Hotkeys.GetValueOrDefault(name, DefaultHotkeys[name])} → {gesture}");
        return string.Join(Environment.NewLine, lines);
    }

    private void SetLayoutPreferences(LayoutPreferencesFile preferences)
    {
        _settings.LayoutMode = preferences.LayoutMode;
        _settings.WindowSizePreset = preferences.Preset;
        _settings.CustomZones = preferences.CustomZones;
        _settings.AppRules = preferences.AppRules;
        _settings.MonitorProfiles = preferences.MonitorProfiles;
    }

    private void LayoutPreferencesChanged()
    {
        _stackedZones = [];
        OnSettingsChanged();
        WindowSizePresetChanged?.Invoke(_settings.WindowSizePreset);
        LayoutModeChanged?.Invoke(_settings.LayoutMode);
    }

    private static bool IsCustomSlot(string slot) => slot.StartsWith("Custom", StringComparison.Ordinal);

    private void ApplyCustomZoneFallbacks(IReadOnlyList<ManagedWindow> windows,
        IReadOnlyList<PlacementDeviation> deviations, IReadOnlyDictionary<IntPtr, MonitorInfo> monitors)
    {
        if (deviations.Count == 0 || monitors.Count == 0) return;
        int moved = 0;
        foreach (var deviation in deviations)
        {
            const int tolerance = PlacementVerifier.TolerancePx;
            var (hwnd, expected, actual) = deviation;
            if (!monitors.TryGetValue(hwnd, out var monitor) || monitor.IsFallback ||
                !Screen.AllScreens.Any(screen => screen.DeviceName == monitor.DeviceName &&
                    screen.WorkingArea == new Rectangle(monitor.WorkArea.Left, monitor.WorkArea.Top,
                        monitor.WorkArea.Width, monitor.WorkArea.Height)))
                continue;
            if (Math.Abs(actual.Left - expected.Left) > tolerance || Math.Abs(actual.Top - expected.Top) > tolerance ||
                (actual.Width <= expected.Width + tolerance && actual.Height <= expected.Height + tolerance))
                continue;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!windows.Any(window => window.Hwnd == hwnd && (uint)window.ProcessId == pid) ||
                !_virtualDesktopService.IsWindowOnCurrentVirtualDesktop(hwnd))
                continue;
            var area = monitor.WorkArea;
            var flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_ASYNCWINDOWPOS;
            if (NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, area.Left, area.Top, area.Width, area.Height, (uint)flags))
                moved++;
        }
        if (moved > 0) _trayController.NotifyCustomFallback(moved);
    }

    private void ShowLayoutError(Exception ex)
    {
        _logger.Warn($"Layout settings operation failed: {ex.Message}");
        MessageBox.Show(ex.Message, "PeekDows layout settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
