using System;
using PeekDows.Core.Models;

namespace PeekDows.App.Tray;

public interface IPeekDowsController
{
    RuntimeState State { get; }
    void ArrangeNow();
    void TogglePause();
    void OpenSettings();
    void Exit();
    event Action<RuntimeState>? StateChanged;
}
