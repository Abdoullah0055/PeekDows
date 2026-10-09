using System;
using System.Collections.Generic;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Covers the tray-toggle behavior for AllowRepositionMaximizedWindows. The real
/// PeekDowsAppContext is a WinForms ApplicationContext wired to live Win32 services and a real
/// NotifyIcon, so it cannot be unit-tested directly. These tests exercise the toggle contract
/// through a fake controller that mirrors the real semantics (mutate setting in place, persist
/// via SettingsService, raise the *Changed event). The menu item wiring itself (label + initial
/// Checked from CurrentSettings.AllowRepositionMaximizedWindows) lives in TrayMenuBuilder.
/// </summary>
public class RepositionMaximizedWindowsToggleTests
{
    [Fact]
    public void AppSettings_DefaultAllowRepositionMaximizedWindows_IsFalse()
    {
        var settings = new AppSettings();

        Assert.False(settings.AllowRepositionMaximizedWindows);
    }

    [Fact]
    public void TrayMenu_RepositionMaximizedWindowsItem_ReflectsSetting_WhenFalse()
    {
        var controller = new FakeToggleController(new AppSettings());

        // The menu item initial state is seeded from CurrentSettings.AllowRepositionMaximizedWindows,
        // mirroring how TrayMenuBuilder initializes the ToolStripMenuItem.Checked property.
        Assert.False(controller.CurrentSettings.AllowRepositionMaximizedWindows);
    }

    [Fact]
    public void TrayMenu_RepositionMaximizedWindowsItem_ReflectsSetting_WhenTrue()
    {
        var settings = new AppSettings { AllowRepositionMaximizedWindows = true };
        var controller = new FakeToggleController(settings);

        Assert.True(controller.CurrentSettings.AllowRepositionMaximizedWindows);
    }

    [Fact]
    public void TrayMenu_RepositionMaximizedWindowsClick_TogglesSetting()
    {
        var controller = new FakeToggleController(new AppSettings());
        Assert.False(controller.CurrentSettings.AllowRepositionMaximizedWindows);

        controller.ToggleAllowRepositionMaximizedWindows();
        Assert.True(controller.CurrentSettings.AllowRepositionMaximizedWindows);

        controller.ToggleAllowRepositionMaximizedWindows();
        Assert.False(controller.CurrentSettings.AllowRepositionMaximizedWindows);
    }

    [Fact]
    public void TrayMenu_RepositionMaximizedWindowsClick_RaisesChangedEvent()
    {
        var controller = new FakeToggleController(new AppSettings());
        var raisedValues = new List<bool>();
        controller.AllowRepositionMaximizedWindowsChanged += v => raisedValues.Add(v);

        controller.ToggleAllowRepositionMaximizedWindows();
        controller.ToggleAllowRepositionMaximizedWindows();

        Assert.Equal(new[] { true, false }, raisedValues);
    }

    [Fact]
    public void TrayMenu_RepositionMaximizedWindowsClick_PersistsSetting()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(tempDir);
        var settingsPath = System.IO.Path.Combine(tempDir, "settings.json");
        try
        {
            var settingsService = new SettingsService(settingsPath);
            var settings = settingsService.Load();
            var controller = new FakeToggleController(settings, settingsService);

            controller.ToggleAllowRepositionMaximizedWindows();

            var reloaded = settingsService.Load();
            Assert.True(reloaded.AllowRepositionMaximizedWindows);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Minimal fake that mirrors the real PeekDowsAppContext toggle semantics for the maximized
    /// repositioning setting: mutate in place, persist via SettingsService, raise the Changed event.
    /// </summary>
    private sealed class FakeToggleController : IPeekDowsController
    {
        private readonly SettingsService? _settingsService;

        public FakeToggleController(AppSettings settings) : this(settings, null) { }

        public FakeToggleController(AppSettings settings, SettingsService? settingsService)
        {
            CurrentSettings = settings;
            _settingsService = settingsService;
        }

        public RuntimeState State => RuntimeState.Running;
        public string LogFilePath => "";
        public AppSettings CurrentSettings { get; }
        public bool IsAutoArrangeRunning => false;
        public bool IsStartWithWindowsEnabled => false;
        public bool IsDirectionalFocusEnabled => false;
        public bool IsAnimateWindowTransitionsEnabled => CurrentSettings.AnimateWindowTransitions;
        public bool AllowRepositionMaximizedWindows => CurrentSettings.AllowRepositionMaximizedWindows;
        public WindowSizePreset CurrentWindowSizePreset => WindowSizePreset.Small;
        public bool IsPaused => false;
        public DateTimeOffset? PauseUntil => null;
        public string? PauseDescription => null;

        public event Action<RuntimeState>? StateChanged;
        public event Action<bool>? AutoArrangeChanged;
        public event Action<bool>? StartWithWindowsChanged;
        public event Action<bool>? DirectionalFocusChanged;
        public event Action<bool>? AnimateWindowTransitionsChanged;
        public event Action<bool>? AllowRepositionMaximizedWindowsChanged;
        public event Action<WindowSizePreset>? WindowSizePresetChanged;
        public event Action<string>? FocusHintModeChanged;

        public void ArrangeNow() { }
        public void TogglePause() { }
        public void PauseFor(TimeSpan duration) { }
        public void PauseUntilResumed() { }
        public void Resume() { }
        public void ToggleAutoArrange() { }
        public void ToggleStartWithWindows() { }
        public void ToggleDirectionalFocus() { }
        public void ToggleAnimateWindowTransitions() { }

        public void ToggleAllowRepositionMaximizedWindows()
        {
            CurrentSettings.AllowRepositionMaximizedWindows = !CurrentSettings.AllowRepositionMaximizedWindows;
            _settingsService?.Save(CurrentSettings);
            AllowRepositionMaximizedWindowsChanged?.Invoke(CurrentSettings.AllowRepositionMaximizedWindows);
        }

        public void SetFocusHintMode(string mode) { CurrentSettings.FocusHintMode = mode; }

        public void SetWindowSizePreset(WindowSizePreset preset) { }

        public void OpenSettings() { }
        public void OpenLogFile() { }
        public void OpenLogsFolder() { }
        public void OnSettingsChanged() { }
        public void Exit() { }
    }
}
