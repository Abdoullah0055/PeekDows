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
    void ArrangeNow();
    void TogglePause();
    void ToggleAutoArrange();
    void OpenSettings();
    void OpenLogFile();
    void OpenLogsFolder();
    void Exit();
    event Action<RuntimeState>? StateChanged;
    event Action<bool>? AutoArrangeChanged;
}
