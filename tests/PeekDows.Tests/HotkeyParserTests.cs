using PeekDows.App.Settings;
using Xunit;

namespace PeekDows.Tests;

public sealed class HotkeyParserTests
{
    [Fact] public void Parse_CtrlAltSpace_Ok()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Space", out var mod, out var vk));
        Assert.Equal(0x0002u | 0x0001u, mod);
        Assert.Equal(0x20u, vk);
    }
    [Fact] public void Parse_CtrlAltP_Ok()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+P", out _, out var vk));
        Assert.Equal(0x50u, vk);
    }
    [Fact] public void Parse_ModifiersOnly_Rejected()
    {
        Assert.False(HotkeyParser.TryParse("Ctrl+Alt", out _, out _));
    }
    [Fact] public void Parse_Empty_Rejected()
    {
        Assert.False(HotkeyParser.TryParse("", out _, out _));
    }
    [Fact] public void RoundTrip_Format_Parse()
    {
        var s = HotkeyParser.Format(0x0002u | 0x0001u, 0x50u);
        Assert.Equal("Ctrl+Alt+P", s);
    }
}
