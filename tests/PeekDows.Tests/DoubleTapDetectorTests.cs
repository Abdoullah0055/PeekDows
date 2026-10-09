using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class DoubleTapDetectorTests
{
    private const int VkControl = 0x11;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int VkA = 0x41;

    private static readonly long TicksPerMs = TimeSpan.TicksPerMillisecond;

    private readonly DoubleTapDetector _detector = new();

    private static long At(long t0, int ms) => t0 + ms * TicksPerMs;

    private TapResult Down(int vk, long t) => _detector.Feed(vk, keyDown: true, nowTicks: t);
    private TapResult Up(int vk, long t) => _detector.Feed(vk, keyDown: false, nowTicks: t);

    [Fact]
    public void SingleTap_ReturnsNone()
    {
        long t0 = 1_000_000_000_000L;
        Assert.Equal(TapResult.None, Down(VkControl, t0));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 50)));
    }

    [Fact]
    public void DoubleTap_100ms_ArmsOnSecondUp()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 100)));
        Assert.Equal(TapResult.Armed, Up(VkControl, At(t0, 150)));
    }

    [Fact]
    public void DoubleTap_Exactly350ms_Arms()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        long firstUp = At(t0, 50);
        Up(VkControl, firstUp);
        Down(VkControl, At(t0, 300));
        Assert.Equal(TapResult.Armed, Up(VkControl, firstUp + 350 * TicksPerMs));
    }

    [Fact]
    public void DoubleTap_349ms_Arms()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        long firstUp = At(t0, 50);
        Up(VkControl, firstUp);
        Down(VkControl, At(t0, 300));
        Assert.Equal(TapResult.Armed, Up(VkControl, firstUp + 349 * TicksPerMs));
    }

    [Fact]
    public void DoubleTap_351ms_ReturnsNone()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        long firstUp = At(t0, 50);
        Up(VkControl, firstUp);
        Down(VkControl, At(t0, 300));
        Assert.Equal(TapResult.None, Up(VkControl, firstUp + 351 * TicksPerMs));
    }

    [Fact]
    public void LeftThenRight_Arms()
    {
        long t0 = 1_000_000_000_000L;
        Assert.Equal(TapResult.None, Down(VkLeftControl, t0));
        Assert.Equal(TapResult.None, Up(VkLeftControl, At(t0, 50)));
        Assert.Equal(TapResult.None, Down(VkRightControl, At(t0, 100)));
        Assert.Equal(TapResult.Armed, Up(VkRightControl, At(t0, 150)));
    }

    [Fact]
    public void GenericThenLeft_Arms()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        Down(VkLeftControl, At(t0, 100));
        Assert.Equal(TapResult.Armed, Up(VkLeftControl, At(t0, 150)));
    }

    [Fact]
    public void OtherKeyDownBetweenTaps_CancelsPending()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        Assert.Equal(TapResult.None, Down(VkA, At(t0, 100)));
        Assert.Equal(TapResult.None, Up(VkA, At(t0, 120)));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 150)));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 200)));
    }

    [Fact]
    public void NonCtrlUp_DoesNotCancelPending()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        Assert.Equal(TapResult.None, Up(VkA, At(t0, 100)));
        Down(VkControl, At(t0, 150));
        Assert.Equal(TapResult.Armed, Up(VkControl, At(t0, 200)));
    }

    [Fact]
    public void Hold600ms_CancelsPending_NextTapIsNone()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 100)));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 700)));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 800)));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 850)));
    }

    [Fact]
    public void HoldAsFirstPress_CreatesNoPending()
    {
        long t0 = 1_000_000_000_000L;
        Assert.Equal(TapResult.None, Down(VkControl, t0));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 600)));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 700)));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 750)));
    }

    [Fact]
    public void RepeatDown_ReturnsNoneAndKeepsPending()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 100)));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 120)));
        Assert.Equal(TapResult.Armed, Up(VkControl, At(t0, 150)));
    }

    [Fact]
    public void OrphanUp_ReturnsNone()
    {
        long t0 = 1_000_000_000_000L;
        Assert.Equal(TapResult.None, Up(VkControl, t0));
        Assert.Equal(TapResult.None, Up(VkLeftControl, At(t0, 10)));
    }

    [Fact]
    public void TripleTap_ArmsOnceThenNone()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        Down(VkControl, At(t0, 100));
        Assert.Equal(TapResult.Armed, Up(VkControl, At(t0, 150)));
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 200)));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 250)));
    }

    [Fact]
    public void Reset_ClearsPending()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        Up(VkControl, At(t0, 50));
        _detector.Reset();
        Assert.Equal(TapResult.None, Down(VkControl, At(t0, 100)));
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 150)));
    }

    [Fact]
    public void Reset_ClearsInProgressDown()
    {
        long t0 = 1_000_000_000_000L;
        Down(VkControl, t0);
        _detector.Reset();
        Assert.Equal(TapResult.None, Up(VkControl, At(t0, 50)));
    }
}
