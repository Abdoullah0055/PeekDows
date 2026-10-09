using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Pure view-model describing whether the Directional Focus hint overlay shows.
/// No Win32, no I/O — fully unit testable.
/// </summary>
public sealed record FocusHintViewModel(
    bool ShowOverlay,
    DirectionalFocusSlot? ActiveSlot,
    bool HasTarget);

/// <summary>
/// Pure resolver mapping (FocusHintMode, detected slot, target presence)
/// to a <see cref="FocusHintViewModel"/>.
/// Only Off and Overlay are valid modes. Any other value (Both, Spotlight,
/// null, unknown, whitespace) maps to Overlay for backward compatibility
/// with pre-V5 settings. Off hides everything; otherwise the overlay shows
/// whenever the gesture is active — even with a null slot or no target
/// (the caller decides to hide when nothing is populated; only the mode flag
/// is kept here).
/// </summary>
public static class FocusHintState
{
    public static FocusHintViewModel Resolve(
        string? mode,
        DirectionalFocusSlot? detectedSlot,
        bool hasTarget)
    {
        string normalized = (mode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "OFF" => "Off",
            _ => "Overlay",
        };

        if (normalized == "Off")
            return new FocusHintViewModel(false, detectedSlot, hasTarget);

        return new FocusHintViewModel(true, detectedSlot, hasTarget);
    }
}
