using System;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class PauseStateService
{
    private RuntimeState _state = RuntimeState.Running;
    private DateTimeOffset? _pauseUntil;

    public RuntimeState State => _state;
    public DateTimeOffset? PauseUntil => _pauseUntil;
    public bool IsPaused => _state == RuntimeState.Paused;

    public event Action<RuntimeState>? StateChanged;

    public void TogglePause()
    {
        if (_state == RuntimeState.Running)
            PauseUntilResumed();
        else
            Resume();
    }

    public void PauseUntilResumed()
    {
        _state = RuntimeState.Paused;
        _pauseUntil = null;
        StateChanged?.Invoke(_state);
    }

    public void PauseFor(TimeSpan duration)
    {
        _state = RuntimeState.Paused;
        _pauseUntil = DateTimeOffset.Now + duration;
        StateChanged?.Invoke(_state);
    }

    public void Resume()
    {
        _state = RuntimeState.Running;
        _pauseUntil = null;
        StateChanged?.Invoke(_state);
    }

    public bool CheckExpired(DateTimeOffset now)
    {
        if (_state != RuntimeState.Paused || _pauseUntil == null)
            return false;

        if (now >= _pauseUntil.Value)
        {
            _state = RuntimeState.Running;
            _pauseUntil = null;
            StateChanged?.Invoke(_state);
            return true;
        }

        return false;
    }

    public string? GetPauseDescription()
    {
        if (_state == RuntimeState.Running)
            return null;

        if (_pauseUntil == null)
            return "Pause enabled until resumed";

        var remaining = _pauseUntil.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
            return null;

        var totalMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
        var formattedTime = _pauseUntil.Value.LocalDateTime.ToString("HH:mm");
        return $"Paused until {formattedTime}";
    }
}
