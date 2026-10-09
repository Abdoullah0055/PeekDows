namespace PeekDows.Core.Services;

/// <summary>
/// Pure, Win32-free detector for the OS Alt+Shift language-switch chord
/// (Alt+Shift toggles the input language). Tracks Alt (L/R/generic) and
/// Shift (L/R/generic) key states plus Ctrl/Win pollution and reports
/// <c>true</c> exactly on the key-DOWN transition that completes a clean
/// Alt+Shift coexistence (no Ctrl, no Win). Never throws.
/// </summary>
/// <remarks>
/// State: per-side Alt/Shift/Ctrl flags (+ generic fallback), Win flags and
/// a sticky <c>_polluted</c> latch set by any Ctrl/Win key-DOWN. The latch
/// clears only on full release of all 4 families, so a chord polluted
/// mid-hold can never fire until every modifier is released. A completion
/// fires only on family-level up→down transition of the second modifier;
/// a repeated DOWN while that family is already down returns false.
/// Key-UPs always return false. Non-modifier keys are ignored.
/// </remarks>
public sealed class AltShiftWatcher
{
    // Virtual-key codes (kept local: this class must stay Win32-free).
    private const int VkMenu = 0x12;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkShift = 0x10;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkControl = 0x11;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private bool _altL;
    private bool _altR;
    private bool _altGeneric;
    private bool _shiftL;
    private bool _shiftR;
    private bool _shiftGeneric;
    private bool _ctrlL;
    private bool _ctrlR;
    private bool _ctrlGeneric;
    private bool _winL;
    private bool _winR;

    private bool _polluted;

    private bool AltDown => _altL || _altR || _altGeneric;
    private bool ShiftDown => _shiftL || _shiftR || _shiftGeneric;
    private bool CtrlDown => _ctrlL || _ctrlR || _ctrlGeneric;
    private bool WinDown => _winL || _winR;

    /// <summary>Clears all tracked state (full resync).</summary>
    public void Reset()
    {
        _altL = _altR = _altGeneric = false;
        _shiftL = _shiftR = _shiftGeneric = false;
        _ctrlL = _ctrlR = _ctrlGeneric = false;
        _winL = _winR = false;
        _polluted = false;
    }

    /// <summary>
    /// Feeds one keyboard event. Returns true exactly on the key-DOWN that
    /// completes a clean Alt+Shift chord (second family going up→down while
    /// the other is held, with no Ctrl/Win down and no pollution latch).
    /// Everything else returns false. Never throws.
    /// </summary>
    public bool Feed(int vk, bool keyDown)
    {
        if (IsAlt(vk))
        {
            if (!keyDown)
            {
                SetAlt(vk, false);
                MaybeUnpollute();
                return false;
            }

            bool altWasDown = AltDown;
            SetAlt(vk, true);
            if (altWasDown)
                return false;
            if (_polluted)
                return false;
            return ShiftDown && !CtrlDown && !WinDown;
        }

        if (IsShift(vk))
        {
            if (!keyDown)
            {
                SetShift(vk, false);
                MaybeUnpollute();
                return false;
            }

            bool shiftWasDown = ShiftDown;
            SetShift(vk, true);
            if (shiftWasDown)
                return false;
            if (_polluted)
                return false;
            return AltDown && !CtrlDown && !WinDown;
        }

        if (IsCtrl(vk))
        {
            SetCtrl(vk, keyDown);
            if (keyDown)
                _polluted = true;
            else
                MaybeUnpollute();
            return false;
        }

        if (IsWin(vk))
        {
            SetWin(vk, keyDown);
            if (keyDown)
                _polluted = true;
            else
                MaybeUnpollute();
            return false;
        }

        return false;
    }

    private void MaybeUnpollute()
    {
        // Full release of all 4 families resyncs: clears the pollution latch
        // so the next clean chord can fire.
        if (!AltDown && !ShiftDown && !CtrlDown && !WinDown)
            _polluted = false;
    }

    private static bool IsAlt(int vk)
        => vk == VkMenu || vk == VkLMenu || vk == VkRMenu;

    private static bool IsShift(int vk)
        => vk == VkShift || vk == VkLShift || vk == VkRShift;

    private static bool IsCtrl(int vk)
        => vk == VkControl || vk == VkLControl || vk == VkRControl;

    private static bool IsWin(int vk)
        => vk == VkLWin || vk == VkRWin;

    private void SetAlt(int vk, bool down)
    {
        if (vk == VkLMenu) _altL = down;
        else if (vk == VkRMenu) _altR = down;
        else _altGeneric = down;
    }

    private void SetShift(int vk, bool down)
    {
        if (vk == VkLShift) _shiftL = down;
        else if (vk == VkRShift) _shiftR = down;
        else _shiftGeneric = down;
    }

    private void SetCtrl(int vk, bool down)
    {
        if (vk == VkLControl) _ctrlL = down;
        else if (vk == VkRControl) _ctrlR = down;
        else _ctrlGeneric = down;
    }

    private void SetWin(int vk, bool down)
    {
        if (vk == VkLWin) _winL = down;
        else if (vk == VkRWin) _winR = down;
    }
}
