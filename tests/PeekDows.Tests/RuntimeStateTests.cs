using PeekDows.Core.Models;
using Xunit;

namespace PeekDows.Tests;

public class RuntimeStateTests
{
    [Fact]
    public void DefaultState_IsRunning()
    {
        var state = RuntimeState.Running;
        Assert.Equal(RuntimeState.Running, state);
    }

    [Fact]
    public void PausedState_IsPaused()
    {
        var state = RuntimeState.Paused;
        Assert.Equal(RuntimeState.Paused, state);
    }

    [Fact]
    public void Toggle_RunningToPaused()
    {
        var state = RuntimeState.Running;
        state = state == RuntimeState.Running ? RuntimeState.Paused : RuntimeState.Running;
        Assert.Equal(RuntimeState.Paused, state);
    }

    [Fact]
    public void Toggle_PausedToRunning()
    {
        var state = RuntimeState.Paused;
        state = state == RuntimeState.Running ? RuntimeState.Paused : RuntimeState.Running;
        Assert.Equal(RuntimeState.Running, state);
    }

    [Fact]
    public void Toggle_DoubleRoundTrip_ReturnsToRunning()
    {
        var state = RuntimeState.Running;
        state = state == RuntimeState.Running ? RuntimeState.Paused : RuntimeState.Running;
        state = state == RuntimeState.Running ? RuntimeState.Paused : RuntimeState.Running;
        Assert.Equal(RuntimeState.Running, state);
    }

    [Fact]
    public void PlacementResult_Defaults_AreZero()
    {
        var result = new PlacementResult();
        Assert.Equal(0, result.AttemptedCount);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Errors);
    }
}
