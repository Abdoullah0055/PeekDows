using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class AutoArrangeDecisionEngineTests
{
    private readonly AutoArrangeDecisionEngine _engine = new();

    private static WindowDiff NoChange() => new()
    {
        Added = [],
        Removed = [],
        Current = []
    };

    private static WindowDiff WithAdded(int count)
    {
        var added = new List<ManagedWindow>();
        for (int i = 0; i < count; i++)
        {
            added.Add(new ManagedWindow
            {
                Hwnd = (IntPtr)(100 + i),
                Title = $"Window {i}",
                ProcessName = "test.exe",
                ClassName = "TestClass",
                FirstSeenAt = DateTime.Now
            });
        }
        return new WindowDiff { Added = added, Removed = [], Current = added };
    }

    private static WindowDiff WithRemoved(int count)
    {
        var removed = new List<ManagedWindow>();
        for (int i = 0; i < count; i++)
        {
            removed.Add(new ManagedWindow
            {
                Hwnd = (IntPtr)(200 + i),
                Title = $"Closed Window {i}",
                ProcessName = "test.exe",
                ClassName = "TestClass",
                FirstSeenAt = DateTime.Now
            });
        }
        return new WindowDiff { Added = [], Removed = removed, Current = [] };
    }

    [Fact]
    public void AutoArrange_Disabled_DoesNothing()
    {
        var diff = WithAdded(1);
        var decision = _engine.Decide(diff, isEnabled: false, isAutoArrange: true, isPaused: false, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_Paused_DoesNothing()
    {
        var diff = WithAdded(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: true, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_SettingFalse_DoesNothing()
    {
        var diff = WithAdded(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: false, isPaused: false, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_AddedWindow_SchedulesArrangeAfterDelay()
    {
        var diff = WithAdded(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.ArrangeAfterDelay, decision);
    }

    [Fact]
    public void AutoArrange_RemovedWindow_ArrangesIfArrangeAfterCloseTrue()
    {
        var diff = WithRemoved(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: false, arrangeAfterWindowCloses: true);
        Assert.Equal(AutoArrangeDecision.ArrangeImmediately, decision);
    }

    [Fact]
    public void AutoArrange_RemovedWindow_DoesNothingIfArrangeAfterCloseFalse()
    {
        var diff = WithRemoved(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: false, arrangeAfterWindowCloses: false);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_AlreadyArranging_DoesNothing()
    {
        var diff = WithAdded(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: true);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_NoChange_DoesNothing()
    {
        var diff = NoChange();
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_AddedAndRemoved_PrioritizesAdded()
    {
        var diff = new WindowDiff
        {
            Added = new List<ManagedWindow>
            {
                new() { Hwnd = (IntPtr)100, Title = "New", ProcessName = "test.exe", ClassName = "T", FirstSeenAt = DateTime.Now }
            },
            Removed = new List<ManagedWindow>
            {
                new() { Hwnd = (IntPtr)200, Title = "Old", ProcessName = "test.exe", ClassName = "T", FirstSeenAt = DateTime.Now }
            },
            Current = []
        };
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.ArrangeAfterDelay, decision);
    }

    [Fact]
    public void AutoArrange_DisabledOverridesEverything()
    {
        var diff = WithAdded(2);
        var decision = _engine.Decide(diff, isEnabled: false, isAutoArrange: true, isPaused: false, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_PausedOverridesAdded()
    {
        var diff = WithAdded(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: true, isAlreadyArranging: false);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_AlreadyArrangingOverridesRemoved()
    {
        var diff = WithRemoved(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: true, arrangeAfterWindowCloses: true);
        Assert.Equal(AutoArrangeDecision.NoOp, decision);
    }

    [Fact]
    public void AutoArrange_RemovedWithArrangeAfterCloseDefault_ArrangesImmediately()
    {
        var settings = new AppSettings();
        Assert.True(settings.ArrangeAfterWindowCloses);

        var diff = WithRemoved(1);
        var decision = _engine.Decide(diff, isEnabled: true, isAutoArrange: true, isPaused: false, isAlreadyArranging: false, settings.ArrangeAfterWindowCloses);
        Assert.Equal(AutoArrangeDecision.ArrangeImmediately, decision);
    }
}
