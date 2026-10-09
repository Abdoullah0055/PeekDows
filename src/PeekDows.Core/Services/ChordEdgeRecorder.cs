namespace PeekDows.Core.Services;

/// <summary>
/// One modifier edge (key DOWN or UP) kept for layout-flip diagnosis.
/// Pure data, no Win32 dependency. <see cref="TicksUtc"/> carries the
/// <c>DateTime.UtcNow.Ticks</c> value passed by the caller at record time.
/// </summary>
public sealed record ChordEdge(int Vk, bool KeyDown, bool Injected, long TicksUtc);

/// <summary>
/// Bounded ring buffer of recent modifier edges so a watcher can tell a
/// key-DOWN from a key-UP when a layout flip is observed. Win32-free and
/// fully unit testable; the hook side calls <see cref="Record"/> and the
/// watcher consumes via <see cref="Drain"/>.
/// </summary>
/// <remarks>
/// Only modifier virtual keys are stored: generic + left/right Ctrl, Shift,
/// Alt and Win (0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0-0xA5). Anything else is
/// ignored. Past <see cref="Capacity"/> the oldest edges are overwritten.
/// Methods never throw.
/// </remarks>
public sealed class ChordEdgeRecorder
{
    /// <summary>Maximum number of edges retained.</summary>
    public const int Capacity = 40;

    private readonly ChordEdge[] _buffer = new ChordEdge[Capacity];
    private int _start;
    private int _count;

    /// <summary>
    /// Records one edge when <paramref name="vk"/> is a modifier; ignores
    /// any other key. <paramref name="ticksUtc"/> is the caller-provided
    /// <c>DateTime.UtcNow.Ticks</c> timestamp (parameter for testability).
    /// Never throws.
    /// </summary>
    public void Record(int vk, bool keyDown, bool injected, long ticksUtc)
    {
        if (!IsModifier(vk))
            return;

        var edge = new ChordEdge(vk, keyDown, injected, ticksUtc);
        if (_count < Capacity)
        {
            _buffer[(_start + _count) % Capacity] = edge;
            _count++;
        }
        else
        {
            _buffer[_start] = edge;
            _start = (_start + 1) % Capacity;
        }
    }

    /// <summary>
    /// Records one edge with the current UTC timestamp. Convenience overload;
    /// prefers the <c>ticksUtc</c> overload in tests. Never throws.
    /// </summary>
    public void Record(int vk, bool keyDown, bool injected)
        => Record(vk, keyDown, injected, DateTime.UtcNow.Ticks);

    /// <summary>
    /// Returns a copy ordered oldest-to-newest and empties the buffer.
    /// Returns an empty list when nothing was recorded. Never throws.
    /// </summary>
    public IReadOnlyList<ChordEdge> Drain()
    {
        if (_count == 0)
            return Array.Empty<ChordEdge>();

        var result = new ChordEdge[_count];
        for (int i = 0; i < result.Length; i++)
            result[i] = _buffer[(_start + i) % Capacity];

        Clear();
        return result;
    }

    /// <summary>Discards all recorded edges. Never throws.</summary>
    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _start = 0;
        _count = 0;
    }

    private static bool IsModifier(int vk)
        => vk == 0x10 // VK_SHIFT (generic)
            || vk == 0x11 // VK_CONTROL (generic)
            || vk == 0x12 // VK_MENU/Alt (generic)
            || vk == 0x5B // VK_LWIN
            || vk == 0x5C // VK_RWIN
            || (vk >= 0xA0 && vk <= 0xA5); // L/R Shift, Control, Menu
}
