namespace PeekDows.Core.Services;

/// <summary>
/// Pure, side-effect-free policy that decides whether the current keyboard
/// modifier combination should activate Directional Focus.
/// </summary>
/// <remarks>
/// Extracted from <see cref="DirectionalFocusGestureDetector"/> so the exact
/// combination rules can be unit tested without Win32 P/Invoke state.
///
/// Activation rule (Ctrl+Win hold gesture):
/// <list type="bullet">
/// <item>Ctrl down AND (left or right) Win down, with Shift and Alt up → active.</item>
/// <item>Ctrl alone → ignored (too easy to trigger by mistake).</item>
/// <item>Win alone → ignored (would collide with the Start menu).</item>
/// <item>Shift held in any combination → ignored (preserves Ctrl+Shift layout
/// toggle and Shift-based shortcuts).</item>
/// <item>Alt held in any combination → ignored (preserves Ctrl+Alt shortcuts
/// and avoids AltGr interference).</item>
/// </list>
/// <remarks>
/// Ctrl+Shift was retired: it collides with the Windows keyboard-layout toggle
/// and re-armed itself through our own Alt activation pulse. Ctrl+Win has no
/// OS layout toggle; any third key disarms the gesture in the service tick.
/// </remarks>
public sealed class DirectionalFocusInputGate
{
    public bool IsGestureModifierActive(bool ctrlDown, bool shiftDown, bool altDown, bool lWinDown, bool rWinDown)
    {
        if (shiftDown) return false;
        if (altDown) return false;
        return ctrlDown && (lWinDown || rWinDown);
    }
}
