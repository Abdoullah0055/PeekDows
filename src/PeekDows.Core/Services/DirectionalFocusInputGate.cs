namespace PeekDows.Core.Services;

/// <summary>
/// Pure, side-effect-free policy that decides whether the current keyboard
/// modifier combination should activate Directional Focus.
/// </summary>
/// <remarks>
/// Extracted from <see cref="DirectionalFocusGestureDetector"/> so the exact
/// combination rules can be unit tested without Win32 P/Invoke state.
///
/// Activation rule (Ctrl+Shift gesture):
/// <list type="bullet">
/// <item>Ctrl down AND Shift down → active.</item>
/// <item>Ctrl alone → ignored (too easy to trigger by mistake).</item>
/// <item>Shift alone → ignored.</item>
/// <item>Alt held in any combination → ignored (preserves Ctrl+Alt shortcuts).</item>
/// <item>Windows key (left or right) held in any combination → ignored, so
/// Ctrl+Win+Arrow virtual-desktop switches never enter the gesture path.</item>
/// </list>
/// </remarks>
public sealed class DirectionalFocusInputGate
{
    public bool IsGestureModifierActive(bool ctrlDown, bool shiftDown, bool altDown, bool lWinDown, bool rWinDown)
    {
        if (altDown) return false;
        if (lWinDown || rWinDown) return false;
        return ctrlDown && shiftDown;
    }
}
