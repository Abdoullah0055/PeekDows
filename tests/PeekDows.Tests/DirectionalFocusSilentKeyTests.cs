using PeekDows.App.Focus;
using Xunit;

namespace PeekDows.Tests;

public class DirectionalFocusSilentKeyTests
{
    [Theory]
    [InlineData(0xAD)] // Volume mute
    [InlineData(0xAE)] // Volume down (killed every gesture in Oct 2026 flicker loop)
    [InlineData(0xAF)] // Volume up
    [InlineData(0xB0)] // Media next
    [InlineData(0xB1)] // Media prev
    [InlineData(0xB2)] // Media stop
    [InlineData(0xB3)] // Media play/pause
    [InlineData(0xA6)] // Browser back
    [InlineData(0xA7)] // Browser forward
    [InlineData(0xA8)] // Browser refresh
    [InlineData(0xA9)] // Browser stop
    [InlineData(0xAA)] // Browser search
    [InlineData(0xAB)] // Browser favorites
    [InlineData(0xAC)] // Browser home
    [InlineData(0xB4)] // Launch mail
    [InlineData(0xB5)] // Launch media select
    [InlineData(0xB6)] // Launch app1
    [InlineData(0xB7)] // Launch app2
    [InlineData(0x14)] // CapsLock
    [InlineData(0x90)] // NumLock
    [InlineData(0x91)] // ScrollLock
    public void SilentKeys_ReturnTrue(int vk)
    {
        Assert.True(DirectionalFocusService.IsSilentKey(vk));
    }

    [Theory]
    [InlineData(0x44)] // D (Ctrl+Win+D must still disarm)
    [InlineData(0x25)] // Left arrow
    [InlineData(0x09)] // Tab
    [InlineData(0x41)] // A (typing)
    [InlineData(0x1B)] // Esc (handled separately, not silent)
    [InlineData(0x11)] // Ctrl (chord, handled separately)
    [InlineData(0x5B)] // LWin (chord, handled separately)
    [InlineData(0x10)] // Shift (handled separately)
    [InlineData(0x12)] // Alt (handled separately)
    public void RealKeys_ReturnFalse(int vk)
    {
        Assert.False(DirectionalFocusService.IsSilentKey(vk));
    }
}
