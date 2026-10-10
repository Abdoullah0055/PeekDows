using System;
using System.Drawing;
using PeekDows.App.Focus;
using Xunit;

namespace PeekDows.Tests;

public sealed class WindowIconServiceTests
{
    private static Icon FreshTestIcon() => (Icon)SystemIcons.Application.Clone();

    [Fact]
    public void GetEntry_Hit_ReturnsSameBitmapInstance_AndExtractsOnce()
    {
        int calls = 0;
        var hwnd = new IntPtr(12345);
        using var svc = new WindowIconService(_ =>
        {
            calls++;
            return (FreshTestIcon(), "FakeApp");
        });

        var first = svc.GetEntry(hwnd);
        var second = svc.GetEntry(hwnd);

        Assert.NotNull(first.Icon);
        Assert.Equal("FakeApp", first.Label);
        Assert.Equal("FakeApp", second.Label);
        Assert.Same(first.Icon, second.Icon);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetEntry_Miss_ReturnsNullAndQuestion()
    {
        using var svc = new WindowIconService(_ => ((Icon?)null, "?"));

        var (icon, label) = svc.GetEntry(new IntPtr(54321));

        Assert.Null(icon);
        Assert.Equal("?", label);
    }

    [Fact]
    public void GetEntry_FallbackLabel_IsReturned()
    {
        using var svc = new WindowIconService(_ => (FreshTestIcon(), "MyApp"));

        var (icon, label) = svc.GetEntry(new IntPtr(111));

        Assert.NotNull(icon);
        Assert.Equal("MyApp", label);
    }

    [Fact]
    public void Clear_ForcesReExtraction()
    {
        int calls = 0;
        var hwnd = new IntPtr(222);
        using var svc = new WindowIconService(_ =>
        {
            calls++;
            return (FreshTestIcon(), "FakeApp");
        });

        var first = svc.GetEntry(hwnd);
        svc.Clear();
        var second = svc.GetEntry(hwnd);

        Assert.Equal(2, calls);
        Assert.NotNull(second.Icon);
        Assert.NotSame(first.Icon, second.Icon);
    }

    [Fact]
    public void GetEntry_AfterDispose_ReturnsNullQuestion_WithoutThrowing()
    {
        var svc = new WindowIconService(_ => (FreshTestIcon(), "FakeApp"));
        svc.Dispose();

        var ex = Record.Exception(() =>
        {
            var entry = svc.GetEntry(new IntPtr(1));
            Assert.Null(entry.Icon);
            Assert.Equal("?", entry.Label);
        });
        Assert.Null(ex);

        var ex2 = Record.Exception(() => svc.Dispose());
        Assert.Null(ex2);
    }

    [Fact]
    public void GetEntry_WhenExtractorThrows_ReturnsNullQuestion()
    {
        using var svc = new WindowIconService(_ => throw new InvalidOperationException("boom"));

        var ex = Record.Exception(() =>
        {
            var entry = svc.GetEntry(new IntPtr(7));
            Assert.Null(entry.Icon);
            Assert.Equal("?", entry.Label);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void GetEntry_DefaultExtractor_InvalidHwnd_ReturnsNull_WithoutThrowing()
    {
        using var svc = new WindowIconService();

        var ex = Record.Exception(() =>
        {
            var entry = svc.GetEntry(IntPtr.Zero);
            Assert.Null(entry.Icon);
        });

        Assert.Null(ex);
    }
}
