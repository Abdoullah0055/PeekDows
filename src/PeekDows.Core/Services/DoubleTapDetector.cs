namespace PeekDows.Core.Services;

/// <summary>
/// Result of feeding a keyboard event to <see cref="DoubleTapDetector"/>.
/// </summary>
public enum TapResult
{
    None,
    Armed,
}

/// <summary>
/// Pure detector for a Ctrl double-tap (generic 0x11, left 0xA2, right 0xA3 —
/// left/right are indifferent, single shared state). This is the trigger for
/// directional focus; held keys no longer trigger anything.
/// </summary>
/// <remarks>
/// Pure: no Win32, no clock access (the caller passes
/// <c>DateTime.UtcNow.Ticks</c>), and <see cref="Feed"/> never throws.
/// A tap is a DOWN followed by an UP. The second tap's UP returns
/// <see cref="TapResult.Armed"/> if and only if it arrives within
/// <see cref="DefaultMaxTapIntervalMs"/> of the first tap's UP.
/// </remarks>
public sealed class DoubleTapDetector
{
    public const int DefaultMaxTapIntervalMs = 350;
    public const int HoldCancelMs = 500;

    private const int VkControl = 0x11;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;

    private bool _ctrlDown;
    private long _downTicks;
    private bool _hasPendingFirstUp;
    private long _firstUpTicks;

    /// <summary>
    /// Feeds one keyboard event. Never throws.
    /// </summary>
    /// <param name="vk">Virtual-key code.</param>
    /// <param name="keyDown">True for key-down, false for key-up.</param>
    /// <param name="nowTicks">Current time in <c>DateTime.UtcNow.Ticks</c>.</param>
    public TapResult Feed(int vk, bool keyDown, long nowTicks)
    {
        if (!IsCtrl(vk))
        {
            if (keyDown)
            {
                // Any other key pressed cancels a pending first tap.
                _hasPendingFirstUp = false;
            }
            else
            {
                // Hold-cancel is verified on every Feed.
                CancelHold(nowTicks);
            }

            return TapResult.None;
        }

        if (keyDown)
        {
            // Auto-repeat: already down → None, pending untouched.
            if (_ctrlDown)
                return TapResult.None;

            _ctrlDown = true;
            _downTicks = nowTicks;
            return TapResult.None;
        }

        // Ctrl UP.
        if (!_ctrlDown)
            return TapResult.None; // Orphan UP.

        _ctrlDown = false;

        long pressTicks = nowTicks - _downTicks;
        if (pressTicks < 0 || pressTicks > HoldCancelMs * TimeSpan.TicksPerMillisecond)
        {
            // Held too long (or clock anomaly): not a tap, cancels any pending first tap.
            _hasPendingFirstUp = false;
            return TapResult.None;
        }

        if (_hasPendingFirstUp)
        {
            long gapTicks = nowTicks - _firstUpTicks;
            if (gapTicks >= 0 && gapTicks <= DefaultMaxTapIntervalMs * TimeSpan.TicksPerMillisecond)
            {
                // State restarts from zero: a 3rd tap begins a new pair.
                _hasPendingFirstUp = false;
                return TapResult.Armed;
            }

            // Outside the window: this tap becomes the new pending first tap.
            _firstUpTicks = nowTicks;
            return TapResult.None;
        }

        _firstUpTicks = nowTicks;
        _hasPendingFirstUp = true;
        return TapResult.None;
    }

    /// <summary>
    /// Clears all internal state.
    /// </summary>
    public void Reset()
    {
        _ctrlDown = false;
        _downTicks = 0;
        _hasPendingFirstUp = false;
        _firstUpTicks = 0;
    }

    private void CancelHold(long nowTicks)
    {
        if (_ctrlDown && nowTicks - _downTicks > HoldCancelMs * TimeSpan.TicksPerMillisecond)
            _hasPendingFirstUp = false;
    }

    private static bool IsCtrl(int vk) => vk == VkControl || vk == VkLeftControl || vk == VkRightControl;
}
