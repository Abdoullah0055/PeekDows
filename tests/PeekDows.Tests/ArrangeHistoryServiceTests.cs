using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>F1 — Undo Arrange tests for <see cref="ArrangeHistoryService"/>.</summary>
public sealed class ArrangeHistoryServiceTests
{
    private static ManagedWindow Window(int hwnd, Rect rect, bool maximized = false) =>
        new() { Hwnd = (IntPtr)hwnd, Title = $"w{hwnd}", CurrentRect = rect, IsMaximized = maximized };

    private static ArrangeHistoryService LiveService() =>
        new(logger: null, isWindowValid: _ => true);

    /// <summary>Records move/maximize calls in one unified sequence to assert ordering.</summary>
    private sealed class OrderRecordingPositioner : IWindowPositioner
    {
        public List<string> Order { get; } = new();

        public (bool success, int win32Error) SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false)
        {
            Order.Add($"move:{hwnd.ToInt64()}");
            return (true, 0);
        }

        public (bool success, int win32Error) MaximizeWindow(IntPtr hwnd)
        {
            Order.Add($"max:{hwnd.ToInt64()}");
            return (true, 0);
        }
    }

    /// <summary>Throws on SetWindowPosition for one hwnd, succeeds otherwise.</summary>
    private sealed class ThrowingPositioner : IWindowPositioner
    {
        private readonly IntPtr _throwHwnd;
        public List<IntPtr> Moves { get; } = new();

        public ThrowingPositioner(IntPtr throwHwnd) => _throwHwnd = throwHwnd;

        public (bool success, int win32Error) SetWindowPosition(IntPtr hwnd, Rect rect, bool bringToFront = false)
        {
            if (hwnd == _throwHwnd)
                throw new InvalidOperationException("fake SetWindowPos boom");
            Moves.Add(hwnd);
            return (true, 0);
        }

        public (bool success, int win32Error) MaximizeWindow(IntPtr hwnd) => (true, 0);
    }

    [Fact]
    public void UndoCancelsOnlySuccessfulPlacementsBeforeRestoringOriginalState()
    {
        var history = LiveService();
        var initial = new Rect(10, 20, 800, 600);
        var target = Window(1, initial);
        var snapshot = history.Capture([target, Window(2, initial)]);
        target.CurrentRect = new Rect(0, 0, 500, 500);
        history.Commit(snapshot, [(IntPtr)1]);
        var fake = new FakeWindowPositioner();

        var cancelled = new List<(IntPtr Handle, int MovesAlreadyMade)>();
        history.Undo(fake, hwnd => cancelled.Add((hwnd, fake.Calls.Count)));

        Assert.Equal(((IntPtr)1, 0), Assert.Single(cancelled));
        Assert.Equal(initial, Assert.Single(fake.Calls).Rect);
    }

    [Fact]
    public void CanUndo_False_WhenEmpty()
    {
        var history = LiveService();

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Push_ThenCanUndo_True()
    {
        var history = LiveService();
        history.Push(new List<ManagedWindow> { Window(1, new Rect(0, 0, 800, 600)) });

        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Undo_RestoresCapturedRect()
    {
        var history = LiveService();
        var rect = new Rect(10, 20, 800, 600);
        history.Push(new List<ManagedWindow> { Window(1, rect) });
        var fake = new FakeWindowPositioner();

        history.Undo(fake);

        Assert.Single(fake.Calls);
        Assert.Equal((IntPtr)1, fake.Calls[0].Hwnd);
        Assert.Equal(rect, fake.Calls[0].Rect);
        Assert.Empty(fake.MaximizeCalls); // not maximized at capture → no re-maximize
        Assert.False(history.CanUndo); // snapshot consumed
    }

    [Fact]
    public void Undo_MaximizedWindow_ReposesBeforeRemaximize()
    {
        var history = LiveService();
        history.Push(new List<ManagedWindow> { Window(7, new Rect(0, 0, 640, 360), maximized: true) });
        var order = new OrderRecordingPositioner();

        history.Undo(order);

        Assert.Equal(new[] { "move:7", "max:7" }, order.Order);
    }

    [Fact]
    public void Undo_DeadHwnd_SkippedSilently()
    {
        var dead = (IntPtr)99;
        var history = new ArrangeHistoryService(logger: null, isWindowValid: hwnd => hwnd != dead);
        history.Push(new List<ManagedWindow>
        {
            Window(1, new Rect(0, 0, 800, 600)),
            new ManagedWindow { Hwnd = dead, Title = "dead", CurrentRect = new Rect(5, 5, 100, 100) },
            Window(2, new Rect(100, 100, 400, 300)),
        });
        var fake = new FakeWindowPositioner();

        var ex = Record.Exception(() => history.Undo(fake));

        Assert.Null(ex);
        Assert.Equal(2, fake.Calls.Count);
        Assert.DoesNotContain(fake.Calls, c => c.Hwnd == dead);
    }

    [Fact]
    public void Push_Cap3_FourthPushEjectsFirst()
    {
        var history = LiveService();
        history.Push(new List<ManagedWindow> { Window(101, new Rect(1, 1, 100, 100)) });
        history.Push(new List<ManagedWindow> { Window(102, new Rect(2, 2, 100, 100)) });
        history.Push(new List<ManagedWindow> { Window(103, new Rect(3, 3, 100, 100)) });
        history.Push(new List<ManagedWindow> { Window(104, new Rect(4, 4, 100, 100)) }); // ejects 101
        var fake = new FakeWindowPositioner();

        history.Undo(fake); // restores 104
        history.Undo(fake); // restores 103
        history.Undo(fake); // restores 102

        Assert.Equal(3, fake.Calls.Count);
        Assert.DoesNotContain(fake.Calls, c => c.Hwnd == (IntPtr)101);
        Assert.Contains(fake.Calls, c => c.Hwnd == (IntPtr)102);
        Assert.Contains(fake.Calls, c => c.Hwnd == (IntPtr)103);
        Assert.Contains(fake.Calls, c => c.Hwnd == (IntPtr)104);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Undo_EmptyHistory_NoThrowNoCalls()
    {
        var history = LiveService();
        var fake = new FakeWindowPositioner();

        var ex = Record.Exception(() => history.Undo(fake));

        Assert.Null(ex);
        Assert.Empty(fake.Calls);
        Assert.Empty(fake.MaximizeCalls);
    }

    [Fact]
    public void Undo_ThrowingPositioner_OtherWindowsStillRestored()
    {
        var history = LiveService();
        history.Push(new List<ManagedWindow>
        {
            Window(1, new Rect(0, 0, 800, 600)),
            Window(2, new Rect(10, 10, 800, 600)),
            Window(3, new Rect(20, 20, 800, 600)),
        });
        var throwing = new ThrowingPositioner((IntPtr)2);

        var ex = Record.Exception(() => history.Undo(throwing));

        Assert.Null(ex); // never throws
        Assert.Equal(new[] { (IntPtr)1, (IntPtr)3 }, throwing.Moves);
    }

    [Fact]
    public void Push_NullOrEmpty_NeverThrowsAndStaysEmpty()
    {
        var history = LiveService();

        var ex = Record.Exception(() =>
        {
            history.Push(null);
            history.Push(new List<ManagedWindow>());
        });

        Assert.Null(ex);
        Assert.False(history.CanUndo);
    }
}
