using PeekDows.Core.Services;

namespace PeekDows.Tests;

/// <summary>
/// Diagnostic tracing: layout-change detection (HKL only), LANGID naming,
/// virtual-key naming. All helpers are pure and never throw.
/// </summary>
public sealed class KeyboardLayoutMonitorTests
{
    [Fact]
    public void HasChanged_Flip_ReturnsTrue()
    {
        var prev = new LayoutSample((nint)0x040C040C, ThreadId: 1);
        var cur = new LayoutSample((nint)0x04090409, ThreadId: 1);
        Assert.True(KeyboardLayoutMonitor.HasChanged(prev, cur));
    }

    [Fact]
    public void HasChanged_SameHklDifferentThread_ReturnsFalse()
    {
        // ThreadId is context only: two threads may share the same HKL.
        var prev = new LayoutSample((nint)0x040C040C, ThreadId: 1);
        var cur = new LayoutSample((nint)0x040C040C, ThreadId: 2);
        Assert.False(KeyboardLayoutMonitor.HasChanged(prev, cur));
    }

    [Fact]
    public void HasChanged_PrevNull_ReturnsFalse()
    {
        var cur = new LayoutSample((nint)0x04090409, ThreadId: 7);
        Assert.False(KeyboardLayoutMonitor.HasChanged(null, cur));
    }

    [Fact]
    public void LangName_FrenchLcid_ReturnsFrFR()
    {
        Assert.Equal("fr-FR", KeyboardLayoutMonitor.LangNameFromHkl((nint)0x040C));
    }

    [Fact]
    public void LangName_EnglishLcid_ReturnsEnUS()
    {
        Assert.Equal("en-US", KeyboardLayoutMonitor.LangNameFromHkl((nint)0x0409));
    }

    [Fact]
    public void LangName_NullHkl_ReturnsFallback()
    {
        Assert.Equal("0x0000", KeyboardLayoutMonitor.LangNameFromHkl((nint)0));
    }

    [Fact]
    public void LangName_InvalidLangId_NeverThrowsReturnsFallback()
    {
        string? name = null;
        var ex = Record.Exception(() => name = KeyboardLayoutMonitor.LangNameFromHkl((nint)0xFFFF));
        Assert.Null(ex);
        Assert.Equal("0xFFFF", name);
    }

    [Theory]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x20, "Space")]
    [InlineData(0x09, "Tab")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x08, "Backspace")]
    [InlineData(0x2E, "Delete")]
    [InlineData(0x25, "Left")]
    [InlineData(0x26, "Up")]
    [InlineData(0x27, "Right")]
    [InlineData(0x28, "Down")]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x4D, "M")]
    [InlineData(0x70, "F1")]
    [InlineData(0x74, "F5")]
    [InlineData(0x87, "F24")]
    [InlineData(0x5B, "Win")]
    public void KeyName_KnownKeys_ReturnsExpected(int vk, string expected)
    {
        Assert.Equal(expected, KeyboardLayoutMonitor.KeyName(vk));
    }

    [Fact]
    public void KeyName_Unknown_ReturnsHex()
    {
        Assert.Equal("0xFF", KeyboardLayoutMonitor.KeyName(0xFF));
    }

    [Fact]
    public void KeyName_NeverThrows()
    {
        var ex = Record.Exception(() =>
        {
            KeyboardLayoutMonitor.KeyName(0);
            KeyboardLayoutMonitor.KeyName(-1);
            KeyboardLayoutMonitor.KeyName(int.MaxValue);
        });
        Assert.Null(ex);
    }
}
