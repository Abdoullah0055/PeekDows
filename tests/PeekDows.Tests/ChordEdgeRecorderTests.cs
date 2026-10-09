using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Bounded modifier-only ring buffer: DOWN vs UP edges for layout-flip
/// diagnosis. Covers capacity/ordering, non-modifier filtering, Drain
/// consumption semantics, Clear and never-throw behavior.
/// </summary>
public sealed class ChordEdgeRecorderTests
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkA = 0x41;
    private const int VkEscape = 0x1B;

    [Fact]
    public void Record_ModifierDownAndUp_BothStoredOldestToNewest()
    {
        var recorder = new ChordEdgeRecorder();

        recorder.Record(VkControl, keyDown: true, injected: false, ticksUtc: 100);
        recorder.Record(VkControl, keyDown: false, injected: false, ticksUtc: 200);

        var edges = recorder.Drain();
        Assert.Equal(2, edges.Count);
        Assert.Equal(new ChordEdge(VkControl, true, false, 100), edges[0]);
        Assert.Equal(new ChordEdge(VkControl, false, false, 200), edges[1]);
    }

    [Fact]
    public void Record_PreservesVkAndInjectedFlag()
    {
        var recorder = new ChordEdgeRecorder();

        recorder.Record(VkShift, keyDown: true, injected: true, ticksUtc: 42);

        var edges = recorder.Drain();
        var single = Assert.Single(edges);
        Assert.Equal(VkShift, single.Vk);
        Assert.True(single.KeyDown);
        Assert.True(single.Injected);
        Assert.Equal(42, single.TicksUtc);
    }

    [Fact]
    public void Capacity_41Records_KeepsLast40InOrder()
    {
        var recorder = new ChordEdgeRecorder();

        for (int i = 0; i < ChordEdgeRecorder.Capacity + 1; i++)
            recorder.Record(VkShift, keyDown: i % 2 == 0, injected: false, ticksUtc: i);

        var edges = recorder.Drain();
        Assert.Equal(ChordEdgeRecorder.Capacity, edges.Count);
        // Oldest (ticksUtc 0) was overwritten; first kept is ticksUtc 1.
        Assert.Equal(1, edges[0].TicksUtc);
        Assert.Equal(ChordEdgeRecorder.Capacity, edges[^1].TicksUtc);
        for (int i = 0; i < edges.Count; i++)
            Assert.Equal(i + 1, edges[i].TicksUtc);
    }

    [Fact]
    public void Capacity_Exactly40_AllKeptInOrder()
    {
        var recorder = new ChordEdgeRecorder();

        for (int i = 0; i < ChordEdgeRecorder.Capacity; i++)
            recorder.Record(VkMenu, keyDown: true, injected: false, ticksUtc: i);

        var edges = recorder.Drain();
        Assert.Equal(ChordEdgeRecorder.Capacity, edges.Count);
        for (int i = 0; i < edges.Count; i++)
            Assert.Equal(i, edges[i].TicksUtc);
    }

    [Theory]
    [InlineData(VkA)]
    [InlineData(VkEscape)]
    [InlineData(0x0D)] // VK_RETURN
    [InlineData(0x20)] // VK_SPACE
    [InlineData(0x70)] // VK_F1
    [InlineData(0x5D)] // VK_APPS (right next to RWin, must stay ignored)
    [InlineData(0x9F)] // just below modifier range
    [InlineData(0xA6)] // just above modifier range
    public void Record_NonModifier_Ignored(int vk)
    {
        var recorder = new ChordEdgeRecorder();

        recorder.Record(vk, keyDown: true, injected: false, ticksUtc: 1);
        recorder.Record(vk, keyDown: false, injected: false, ticksUtc: 2);

        Assert.Empty(recorder.Drain());
    }

    [Theory]
    [InlineData(0x10)] // VK_SHIFT generic
    [InlineData(0x11)] // VK_CONTROL generic
    [InlineData(0x12)] // VK_MENU generic
    [InlineData(0x5B)] // VK_LWIN
    [InlineData(0x5C)] // VK_RWIN
    [InlineData(0xA0)] // VK_LSHIFT
    [InlineData(0xA1)] // VK_RSHIFT
    [InlineData(0xA2)] // VK_LCONTROL
    [InlineData(0xA3)] // VK_RCONTROL
    [InlineData(0xA4)] // VK_LMENU
    [InlineData(0xA5)] // VK_RMENU
    public void Record_AllModifierVks_AreStored(int vk)
    {
        var recorder = new ChordEdgeRecorder();

        recorder.Record(vk, keyDown: true, injected: false, ticksUtc: 7);

        var single = Assert.Single(recorder.Drain());
        Assert.Equal(vk, single.Vk);
    }

    [Fact]
    public void Drain_Empty_ReturnsEmptyList()
    {
        var recorder = new ChordEdgeRecorder();

        var edges = recorder.Drain();

        Assert.NotNull(edges);
        Assert.Empty(edges);
    }

    [Fact]
    public void Drain_ConsumesBuffer_SecondDrainIsEmpty()
    {
        var recorder = new ChordEdgeRecorder();
        recorder.Record(VkControl, keyDown: true, injected: false, ticksUtc: 1);

        var first = recorder.Drain();
        Assert.Single(first);

        Assert.Empty(recorder.Drain());
    }

    [Fact]
    public void Drain_ReturnsCopy_RecordAfterDrainStartsFresh()
    {
        var recorder = new ChordEdgeRecorder();
        recorder.Record(VkShift, keyDown: true, injected: false, ticksUtc: 1);
        var first = recorder.Drain();

        recorder.Record(VkShift, keyDown: false, injected: false, ticksUtc: 2);
        var second = recorder.Drain();

        Assert.Single(first);
        Assert.Equal(1, first[0].TicksUtc);
        var single = Assert.Single(second);
        Assert.Equal(2, single.TicksUtc);
        Assert.False(single.KeyDown);
    }

    [Fact]
    public void Clear_DiscardsEdges()
    {
        var recorder = new ChordEdgeRecorder();
        recorder.Record(VkControl, keyDown: true, injected: false, ticksUtc: 1);
        recorder.Record(VkShift, keyDown: true, injected: false, ticksUtc: 2);

        recorder.Clear();

        Assert.Empty(recorder.Drain());
    }

    [Fact]
    public void Clear_Empty_DoesNothing()
    {
        var recorder = new ChordEdgeRecorder();

        recorder.Clear();

        Assert.Empty(recorder.Drain());
    }

    [Fact]
    public void Clear_AfterOverflow_ResetsToEmpty()
    {
        var recorder = new ChordEdgeRecorder();
        for (int i = 0; i < ChordEdgeRecorder.Capacity + 5; i++)
            recorder.Record(VkLWin, keyDown: true, injected: false, ticksUtc: i);

        recorder.Clear();

        Assert.Empty(recorder.Drain());
        recorder.Record(VkRWin, keyDown: true, injected: false, ticksUtc: 999);
        var single = Assert.Single(recorder.Drain());
        Assert.Equal(999, single.TicksUtc);
    }

    [Fact]
    public void NeverThrows_OnAnyInput()
    {
        var recorder = new ChordEdgeRecorder();

        var ex = Record.Exception(() =>
        {
            recorder.Record(int.MinValue, keyDown: true, injected: false, ticksUtc: long.MinValue);
            recorder.Record(-1, keyDown: false, injected: true, ticksUtc: -1);
            recorder.Record(0, keyDown: true, injected: false, ticksUtc: 0);
            recorder.Record(VkA, keyDown: true, injected: false, ticksUtc: 1);
            recorder.Record(VkEscape, keyDown: false, injected: true, ticksUtc: 2);
            recorder.Record(int.MaxValue, keyDown: true, injected: true, ticksUtc: long.MaxValue);
            recorder.Record(VkShift, keyDown: true, injected: false, ticksUtc: 3);
            recorder.Drain();
            recorder.Drain();
            recorder.Clear();
            recorder.Clear();
            recorder.Drain();
        });

        Assert.Null(ex);
    }
}
