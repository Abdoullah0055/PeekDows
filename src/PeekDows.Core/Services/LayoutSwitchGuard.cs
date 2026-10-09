namespace PeekDows.Core.Services;

/// <summary>
/// Decision returned by <see cref="LayoutSwitchGuard.Feed"/> for a low-level
/// keyboard event. <see cref="GuardDecision.Pass"/> lets the event through to
/// Windows; <see cref="GuardDecision.Swallow"/> blocks it (hook returns 1).
/// Only Ctrl/Shift key-UPs of a pure chord are ever swallowed — key-DOWNs
/// always pass so gestures and shortcuts keep working.
/// </summary>
public enum GuardDecision
{
    Pass,
    Swallow
}

/// <summary>
/// Pure, side-effect-free policy that prevents Windows from seeing the
/// Ctrl+Shift "toggle keyboard layout" chord complete while a Directional
/// Focus gesture is held. Windows flips FR↔EN when one modifier of a pure
/// Ctrl+Shift hold is released; swallowing those key-UPs keeps the layout.
/// Has no Win32 dependency so it is fully unit testable; the
/// WH_KEYBOARD_LL service (<c>KeyboardLayoutGuardService</c>, App-side) feeds
/// it and enforces <see cref="GuardDecision.Swallow"/>.
/// </summary>
/// <remarks>
/// State: per-side Ctrl/Shift/Alt flags (+ generic fallback), Win flags,
/// <c>_otherKeySeen</c> (a non-modifier went down during the hold) and a
/// sticky <c>_pureChordSeen</c> latch set when Ctrl+Shift are jointly down
/// with no Alt/Win/other. The latch is what lets BOTH key-UPs of a held
/// chord be swallowed: after the first release only one modifier is still
/// down, so a purely instantaneous "both down" check would let the second
/// release through. The latch clears on any Alt/Win/other key-DOWN and when
/// no Ctrl/Shift remains down (auto-resync).
/// </remarks>
public sealed class LayoutSwitchGuard
{
    // Virtual-key codes (kept local: this class must stay Win32-free).
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private bool _ctrlL;
    private bool _ctrlR;
    private bool _ctrlGeneric;
    private bool _shiftL;
    private bool _shiftR;
    private bool _shiftGeneric;
    private bool _altL;
    private bool _altR;
    private bool _altGeneric;
    private bool _winL;
    private bool _winR;

    private bool _otherKeySeen;
    private bool _pureChordSeen;

    private bool CtrlDown => _ctrlL || _ctrlR || _ctrlGeneric;
    private bool ShiftDown => _shiftL || _shiftR || _shiftGeneric;
    private bool AltDown => _altL || _altR || _altGeneric;
    private bool WinDown => _winL || _winR;

    /// <summary>Clears all tracked state (full resync).</summary>
    public void Reset()
    {
        _ctrlL = _ctrlR = _ctrlGeneric = false;
        _shiftL = _shiftR = _shiftGeneric = false;
        _altL = _altR = _altGeneric = false;
        _winL = _winR = false;
        _otherKeySeen = false;
        _pureChordSeen = false;
    }

    /// <summary>
    /// Feeds one keyboard event and returns whether the hook must swallow it.
    /// Key-DOWNs always return <see cref="GuardDecision.Pass"/> (state is still
    /// updated). A Ctrl/Shift key-UP returns <see cref="GuardDecision.Swallow"/>
    /// only when the hold was a pure Ctrl+Shift chord (latch set, no Alt/Win,
    /// no other key down) and both <paramref name="directionalFocusEnabled"/>
    /// and <paramref name="guardEnabled"/> are true.
    /// </summary>
    public GuardDecision Feed(int vk, bool keyDown, bool directionalFocusEnabled, bool guardEnabled)
    {
        if (IsCtrl(vk))
        {
            if (keyDown)
            {
                SetCtrl(vk, true);
                NoteModifierDown();
                return GuardDecision.Pass;
            }

            // Key-UP: decide BEFORE clearing so the latch/flags reflect the hold,
            // then clear and auto-resync when no Ctrl/Shift remains down.
            var decision = ShouldSwallow(directionalFocusEnabled, guardEnabled)
                ? GuardDecision.Swallow
                : GuardDecision.Pass;
            SetCtrl(vk, false);
            MaybeAutoReset();
            return decision;
        }

        if (IsShift(vk))
        {
            if (keyDown)
            {
                SetShift(vk, true);
                NoteModifierDown();
                return GuardDecision.Pass;
            }

            var decision = ShouldSwallow(directionalFocusEnabled, guardEnabled)
                ? GuardDecision.Swallow
                : GuardDecision.Pass;
            SetShift(vk, false);
            MaybeAutoReset();
            return decision;
        }

        if (IsAlt(vk))
        {
            // Alt in any combination breaks chord purity; never swallowed.
            SetAlt(vk, keyDown);
            if (keyDown)
                _pureChordSeen = false;
            return GuardDecision.Pass;
        }

        if (IsWin(vk))
        {
            // Win in any combination breaks chord purity; never swallowed.
            SetWin(vk, keyDown);
            if (keyDown)
                _pureChordSeen = false;
            return GuardDecision.Pass;
        }

        // Non-modifier: only key-DOWNs pollute the chord. Key-UPs are ignored
        // (the flag clears on full Ctrl/Shift release).
        if (keyDown)
        {
            _otherKeySeen = true;
            _pureChordSeen = false;
        }

        return GuardDecision.Pass;
    }

    /// <summary>
    /// Returns the <see cref="ShouldSwallow"/> condition without consuming any
    /// event: true when a pure Ctrl+Shift chord is currently held (latch set,
    /// no Alt/Win, no other key) and both flags are on. Side-effect-free.
    /// </summary>
    public bool IsChordArmed(bool directionalFocusEnabled, bool guardEnabled)
        => ShouldSwallow(directionalFocusEnabled, guardEnabled);

    private bool ShouldSwallow(bool directionalFocusEnabled, bool guardEnabled)
        => _pureChordSeen
           && !AltDown
           && !WinDown
           && !_otherKeySeen
           && directionalFocusEnabled
           && guardEnabled;

    private void NoteModifierDown()
    {
        // Latch purity the moment Ctrl+Shift coexist without Alt/Win/other.
        if (CtrlDown && ShiftDown && !AltDown && !WinDown && !_otherKeySeen)
            _pureChordSeen = true;
    }

    private void MaybeAutoReset()
    {
        // Full Ctrl/Shift release resyncs: a stuck otherKeySeen (e.g. a missed
        // Esc-up) can never poison the next gesture. Alt/Win flags are kept as
        // tracked — they clear on their own key-UPs.
        if (!CtrlDown && !ShiftDown)
        {
            _otherKeySeen = false;
            _pureChordSeen = false;
        }
    }

    private static bool IsCtrl(int vk)
        => vk == VkControl || vk == VkLControl || vk == VkRControl;

    private static bool IsShift(int vk)
        => vk == VkShift || vk == VkLShift || vk == VkRShift;

    private static bool IsAlt(int vk)
        => vk == VkMenu || vk == VkLMenu || vk == VkRMenu;

    private static bool IsWin(int vk)
        => vk == VkLWin || vk == VkRWin;

    private void SetCtrl(int vk, bool down)
    {
        if (vk == VkLControl) _ctrlL = down;
        else if (vk == VkRControl) _ctrlR = down;
        else _ctrlGeneric = down;
    }

    private void SetShift(int vk, bool down)
    {
        if (vk == VkLShift) _shiftL = down;
        else if (vk == VkRShift) _shiftR = down;
        else _shiftGeneric = down;
    }

    private void SetAlt(int vk, bool down)
    {
        if (vk == VkLMenu) _altL = down;
        else if (vk == VkRMenu) _altR = down;
        else _altGeneric = down;
    }

    private void SetWin(int vk, bool down)
    {
        if (vk == VkLWin) _winL = down;
        else if (vk == VkRWin) _winR = down;
    }
}
