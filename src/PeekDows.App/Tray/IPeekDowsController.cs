using System;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Tray;

public interface IPeekDowsController
{
    RuntimeState State { get; }
    string LogFilePath { get; }
    AppSettings CurrentSettings { get; }
    bool IsAutoArrangeRunning { get; }
    bool IsStartWithWindowsEnabled { get; }
    bool IsDirectionalFocusEnabled { get; }
    bool IsAnimateWindowTransitionsEnabled { get; }
    bool AllowRepositionMaximizedWindows { get; }
    WindowSizePreset CurrentWindowSizePreset { get; }
    bool IsPaused { get; }
    DateTimeOffset? PauseUntil { get; }
    string? PauseDescription { get; }
    void ArrangeNow();
    void TogglePause();
    void PauseFor(TimeSpan duration);
    void PauseUntilResumed();
    void Resume();
    void ToggleAutoArrange();
    void ToggleStartWithWindows();
    void ToggleDirectionalFocus();
    void ToggleAnimateWindowTransitions();
    void ToggleAllowRepositionMaximizedWindows();
    void SetWindowSizePreset(WindowSizePreset preset);
    void OpenSettings();
    void OpenLogFile();
    void OpenLogsFolder();
    void OnSettingsChanged();
    void Exit();
    bool TryUpdateHotkey(string name, string gesture, out string error)
    {
        error = "not-implemented";
        return false;
    }
    System.Collections.Generic.IReadOnlyList<Settings.WindowRow> GetWindowsSnapshot()
        => System.Array.Empty<Settings.WindowRow>();
    event Action<RuntimeState>? StateChanged;
    event Action<bool>? AutoArrangeChanged;
    event Action<bool>? StartWithWindowsChanged;
    event Action<bool>? DirectionalFocusChanged;
    event Action<bool>? AnimateWindowTransitionsChanged;
    event Action<bool>? AllowRepositionMaximizedWindowsChanged;
    event Action<WindowSizePreset>? WindowSizePresetChanged;
}
