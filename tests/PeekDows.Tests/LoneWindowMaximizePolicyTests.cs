using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class LoneWindowMaximizePolicyTests
{
    private static readonly Rect Laptop = new(0, 0, 1280, 720);
    private static readonly Rect External = new(1920, 0, 1920, 1080);

    [Fact]
    public void ShouldMaximizeLoneWindow_DefaultsTrue()
    {
        var policy = new LoneWindowMaximizePolicy();
        Assert.True(policy.ShouldMaximizeLoneWindow(Laptop));
    }

    [Fact]
    public void NotifyUserRestored_SuppressesMaximize_UntilCountChanges()
    {
        var policy = new LoneWindowMaximizePolicy();
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [Laptop] = 1 });
        policy.NotifyUserRestored((IntPtr)1, Laptop);

        Assert.False(policy.ShouldMaximizeLoneWindow(Laptop));

        // A window opens on the laptop: count 1 → 2 resets the flag…
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [Laptop] = 2 });
        // …then it closes again: 2 → 1. The normal lone-window rule applies again.
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [Laptop] = 1 });
        Assert.True(policy.ShouldMaximizeLoneWindow(Laptop));
    }

    [Fact]
    public void UserRestored_IsPerMonitor()
    {
        var policy = new LoneWindowMaximizePolicy();
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [Laptop] = 1, [External] = 1 });
        policy.NotifyUserRestored((IntPtr)1, Laptop);

        Assert.False(policy.ShouldMaximizeLoneWindow(Laptop));
        Assert.True(policy.ShouldMaximizeLoneWindow(External));
    }

    [Fact]
    public void OnArrangeStarting_SameCount_DoesNotResetUserRestored()
    {
        var policy = new LoneWindowMaximizePolicy();
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [Laptop] = 1 });
        policy.NotifyUserRestored((IntPtr)1, Laptop);
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [Laptop] = 1 });

        Assert.False(policy.ShouldMaximizeLoneWindow(Laptop));
    }

    [Fact]
    public void PeekDowsMaximized_TrackedUntilRestoredByPeekDows()
    {
        var policy = new LoneWindowMaximizePolicy();
        policy.NotifyMaximizedByPeekDows((IntPtr)1);
        Assert.True(policy.WasMaximizedByPeekDows((IntPtr)1));

        policy.NotifyRestoredByPeekDows((IntPtr)1);
        Assert.False(policy.WasMaximizedByPeekDows((IntPtr)1));
    }

    [Fact]
    public void PruneMaximizedSet_RemovesUnknownHwnds()
    {
        var policy = new LoneWindowMaximizePolicy();
        policy.NotifyMaximizedByPeekDows((IntPtr)1);
        policy.NotifyMaximizedByPeekDows((IntPtr)2);

        policy.PruneMaximizedSet([(IntPtr)1]);

        Assert.True(policy.WasMaximizedByPeekDows((IntPtr)1));
        Assert.False(policy.WasMaximizedByPeekDows((IntPtr)2));
    }

    [Fact]
    public void OnArrangeStarting_MonitorDisappears_UserRestoredStateCleared()
    {
        var policy = new LoneWindowMaximizePolicy();
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [Laptop] = 1 });
        policy.NotifyUserRestored((IntPtr)1, Laptop);
        Assert.False(policy.ShouldMaximizeLoneWindow(Laptop));

        // Monitor no longer has arrangeable windows at all: stale state must not survive.
        policy.OnArrangeStarting(new Dictionary<Rect, int> { [External] = 3 });
        Assert.True(policy.ShouldMaximizeLoneWindow(Laptop));
    }
}
