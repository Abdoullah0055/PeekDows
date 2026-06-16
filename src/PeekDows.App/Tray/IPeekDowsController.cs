using System;
using PeekDows.Core.Models;

namespace PeekDows.App.Tray;

public interface IPeekDowsController
{
    RuntimeState State { get; }
    string LogFilePath { get; }
    void ArrangeNow();
    void TogglePause();
    void OpenSettings();
    void OpenLogFile();
    void OpenLogsFolder();
    void Exit();
    event Action<RuntimeState>? StateChanged;
}
