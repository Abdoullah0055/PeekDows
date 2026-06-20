using System;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class UnstableWindowTrackerTests
{
    [Fact]
    public void FreshTracker_ReportsNothingUnstable()
    {
        var t = new UnstableWindowTracker(() => DateTime.Now, null);
        Assert.False(t.IsUnstable((IntPtr)1));
    }

    [Fact]
    public void MarkUnstable_ReportsUnstableUntilCooldownExpires()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var t = new UnstableWindowTracker(() => now, null);

        t.MarkUnstable((IntPtr)1, reason: "slow-setwindowpos");

        Assert.True(t.IsUnstable((IntPtr)1, now));
        Assert.True(t.IsUnstable((IntPtr)1, now.AddMilliseconds(14999)));
        Assert.False(t.IsUnstable((IntPtr)1, now.AddMilliseconds(15001)));
    }

    [Fact]
    public void MarkUnstable_RecordsReason()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var t = new UnstableWindowTracker(() => now, null);

        t.MarkUnstable((IntPtr)1, reason: "not-responding");

        Assert.True(t.TryGetReason((IntPtr)1, out var reason));
        Assert.Equal("not-responding", reason);
    }

    [Fact]
    public void MultipleHwnds_AreIndependent()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var t = new UnstableWindowTracker(() => now, null);

        t.MarkUnstable((IntPtr)1);

        Assert.True(t.IsUnstable((IntPtr)1));
        Assert.False(t.IsUnstable((IntPtr)2));

        t.MarkUnstable((IntPtr)2);
        Assert.True(t.IsUnstable((IntPtr)2));
    }

    [Fact]
    public void HasRecentUnstable_TrueWithinWindow_FalseOutside()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var t = new UnstableWindowTracker(() => now, null);

        Assert.False(t.HasRecentUnstable(withinMs: 1000));

        t.MarkUnstable((IntPtr)1, now: now);
        Assert.True(t.HasRecentUnstable(now: now, withinMs: 1000));
        Assert.True(t.HasRecentUnstable(now: now.AddMilliseconds(500), withinMs: 1000));
        // Just outside the recent window: no longer "recently" marked (but still unstable).
        Assert.False(t.HasRecentUnstable(now: now.AddMilliseconds(1001), withinMs: 1000));
    }

    [Fact]
    public void HasRecentUnstable_False_AfterCooldownFullyExpires()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var t = new UnstableWindowTracker(() => now, null);

        t.MarkUnstable((IntPtr)1, now: now);
        // Past the cooldown: neither unstable nor recent.
        var later = now.AddMilliseconds(20000);
        Assert.False(t.IsUnstable((IntPtr)1, later));
        Assert.False(t.HasRecentUnstable(now: later, withinMs: 1000));
    }
}
