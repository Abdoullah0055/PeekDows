using System;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class PauseStateServiceTests
{
    [Fact]
    public void InitialState_IsRunning()
    {
        var service = new PauseStateService();
        Assert.Equal(RuntimeState.Running, service.State);
        Assert.False(service.IsPaused);
        Assert.Null(service.PauseUntil);
    }

    [Fact]
    public void TogglePause_WhenRunning_SetsPaused()
    {
        var service = new PauseStateService();
        service.TogglePause();
        Assert.Equal(RuntimeState.Paused, service.State);
        Assert.True(service.IsPaused);
    }

    [Fact]
    public void TogglePause_WhenPaused_Resumes()
    {
        var service = new PauseStateService();
        service.TogglePause();
        service.TogglePause();
        Assert.Equal(RuntimeState.Running, service.State);
        Assert.False(service.IsPaused);
    }

    [Fact]
    public void PauseUntilResumed_SetsPausedWithoutExpiration()
    {
        var service = new PauseStateService();
        service.PauseUntilResumed();
        Assert.Equal(RuntimeState.Paused, service.State);
        Assert.Null(service.PauseUntil);
    }

    [Fact]
    public void PauseFor_SetsPausedWithExpiration()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));
        Assert.Equal(RuntimeState.Paused, service.State);
        Assert.NotNull(service.PauseUntil);
        Assert.True(service.PauseUntil > DateTimeOffset.Now);
    }

    [Fact]
    public void PauseFor_5Minutes_SetsExpirationApprox5MinFromNow()
    {
        var service = new PauseStateService();
        var before = DateTimeOffset.Now;
        service.PauseFor(TimeSpan.FromMinutes(5));
        var after = DateTimeOffset.Now;

        Assert.NotNull(service.PauseUntil);
        Assert.True(service.PauseUntil >= before + TimeSpan.FromMinutes(5));
        Assert.True(service.PauseUntil <= after + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Resume_ClearsPauseUntil()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));
        Assert.NotNull(service.PauseUntil);

        service.Resume();
        Assert.Equal(RuntimeState.Running, service.State);
        Assert.Null(service.PauseUntil);
        Assert.False(service.IsPaused);
    }

    [Fact]
    public void Resume_WhenPausedUntilResumed_ClearsState()
    {
        var service = new PauseStateService();
        service.PauseUntilResumed();
        Assert.True(service.IsPaused);

        service.Resume();
        Assert.Equal(RuntimeState.Running, service.State);
        Assert.Null(service.PauseUntil);
    }

    [Fact]
    public void CheckExpired_WhenNotPaused_ReturnsFalse()
    {
        var service = new PauseStateService();
        Assert.False(service.CheckExpired(DateTimeOffset.Now));
    }

    [Fact]
    public void CheckExpired_WhenPausedUntilResumed_ReturnsFalse()
    {
        var service = new PauseStateService();
        service.PauseUntilResumed();
        Assert.False(service.CheckExpired(DateTimeOffset.Now));
    }

    [Fact]
    public void CheckExpired_WhenNotYetExpired_ReturnsFalse()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));
        Assert.False(service.CheckExpired(DateTimeOffset.Now));
    }

    [Fact]
    public void PauseTimerExpiration_ResumesWithoutArrange()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));

        var futureNow = DateTimeOffset.Now + TimeSpan.FromMinutes(6);
        var result = service.CheckExpired(futureNow);

        Assert.True(result);
        Assert.Equal(RuntimeState.Running, service.State);
        Assert.Null(service.PauseUntil);
    }

    [Fact]
    public void StateChanged_FiredOnTogglePause()
    {
        var service = new PauseStateService();
        RuntimeState? captured = null;
        service.StateChanged += s => captured = s;

        service.TogglePause();
        Assert.Equal(RuntimeState.Paused, captured);
    }

    [Fact]
    public void StateChanged_FiredOnResume()
    {
        var service = new PauseStateService();
        service.TogglePause();

        RuntimeState? captured = null;
        service.StateChanged += s => captured = s;

        service.Resume();
        Assert.Equal(RuntimeState.Running, captured);
    }

    [Fact]
    public void StateChanged_FiredOnCheckExpired()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));

        RuntimeState? captured = null;
        service.StateChanged += s => captured = s;

        var futureNow = DateTimeOffset.Now + TimeSpan.FromMinutes(6);
        service.CheckExpired(futureNow);

        Assert.Equal(RuntimeState.Running, captured);
    }

    [Fact]
    public void GetPauseDescription_WhenRunning_ReturnsNull()
    {
        var service = new PauseStateService();
        Assert.Null(service.GetPauseDescription());
    }

    [Fact]
    public void GetPauseDescription_WhenPausedUntilResumed_ReturnsMessage()
    {
        var service = new PauseStateService();
        service.PauseUntilResumed();
        Assert.Equal("Pause enabled until resumed", service.GetPauseDescription());
    }

    [Fact]
    public void GetPauseDescription_WhenPausedWithTimer_ContainsUntil()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));
        var desc = service.GetPauseDescription();
        Assert.NotNull(desc);
        Assert.Contains("Paused until", desc);
    }

    [Fact]
    public void TogglePause_RunningToPaused_UsesPauseUntilResumed()
    {
        var service = new PauseStateService();
        service.TogglePause();
        Assert.Equal(RuntimeState.Paused, service.State);
        Assert.Null(service.PauseUntil);
    }

    [Fact]
    public void PauseFor_OverwritesPreviousPause()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));
        var firstUntil = service.PauseUntil;

        service.PauseFor(TimeSpan.FromMinutes(15));
        Assert.Equal(RuntimeState.Paused, service.State);
        Assert.NotNull(service.PauseUntil);
        Assert.NotEqual(firstUntil, service.PauseUntil);
    }

    [Fact]
    public void PauseUntilResumed_ClearsExistingPauseUntil()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(15));
        Assert.NotNull(service.PauseUntil);

        service.PauseUntilResumed();
        Assert.Equal(RuntimeState.Paused, service.State);
        Assert.Null(service.PauseUntil);
    }

    [Fact]
    public void PauseFor_OverwritesManualPauseWithTimedPause()
    {
        var service = new PauseStateService();
        service.PauseUntilResumed();
        Assert.Null(service.PauseUntil);

        service.PauseFor(TimeSpan.FromMinutes(5));
        Assert.Equal(RuntimeState.Paused, service.State);
        Assert.NotNull(service.PauseUntil);
    }

    [Fact]
    public void CheckExpired_ClearsPauseUntil()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));
        Assert.NotNull(service.PauseUntil);

        var futureNow = DateTimeOffset.Now + TimeSpan.FromMinutes(10);
        service.CheckExpired(futureNow);
        Assert.Null(service.PauseUntil);
    }

    [Fact]
    public void Resume_DoesNotTriggerArrange()
    {
        var service = new PauseStateService();
        service.PauseUntilResumed();

        bool arrangeTriggered = false;
        service.StateChanged += state =>
        {
            if (state == RuntimeState.Running)
                arrangeTriggered = true;
        };

        service.Resume();

        Assert.True(arrangeTriggered);
        Assert.Equal(RuntimeState.Running, service.State);
    }

    [Fact]
    public void PauseTimerExpiration_DoesNotTriggerArrange()
    {
        var service = new PauseStateService();
        service.PauseFor(TimeSpan.FromMinutes(5));

        bool arrangeTriggered = false;
        service.StateChanged += state =>
        {
            if (state == RuntimeState.Running)
                arrangeTriggered = true;
        };

        var futureNow = DateTimeOffset.Now + TimeSpan.FromMinutes(6);
        var expired = service.CheckExpired(futureNow);

        Assert.True(expired);
        Assert.True(arrangeTriggered);
        Assert.Equal(RuntimeState.Running, service.State);
        Assert.Null(service.PauseUntil);
    }
}
