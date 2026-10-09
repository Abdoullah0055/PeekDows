namespace PeekDows.Core.Services;

/// <summary>
/// Immutable snapshot of the foreground keyboard layout (HKL + owning thread).
/// ThreadId is context only; <see cref="KeyboardLayoutMonitor.HasChanged"/>
/// compares HKL alone (two threads may share the same HKL).
/// </summary>
public sealed record LayoutSample(nint Hkl, uint ThreadId);

/// <summary>
/// Diagnostic helpers for keyboard-layout tracing and key naming.
/// Pure, side-effect-free and never throwing (fallbacks instead).
/// </summary>
public static class KeyboardLayoutMonitor
{
    /// <summary>
    /// True iff a previous sample exists and the HKL differs.
    /// ThreadId is ignored by design.
    /// </summary>
    public static bool HasChanged(LayoutSample? prev, LayoutSample cur)
        => prev is not null && prev.Hkl != cur.Hkl;

    /// <summary>
    /// Low word of the HKL is the LANGID. Returns the culture name
    /// (e.g. "fr-FR") or a hex fallback ("0x040C"-style, "0x0000" for null HKL).
    /// Never throws.
    /// </summary>
    public static string LangNameFromHkl(nint hkl)
    {
        try
        {
            ushort langId = unchecked((ushort)(hkl.ToInt64() & 0xFFFF));
            string name = new System.Globalization.CultureInfo(langId).Name;
            return string.IsNullOrEmpty(name) ? $"0x{langId:X4}" : name;
        }
        catch
        {
            ushort langId;
            try
            {
                langId = unchecked((ushort)(hkl.ToInt64() & 0xFFFF));
            }
            catch
            {
                return "0x0000";
            }

            return $"0x{langId:X4}";
        }
    }

    /// <summary>
    /// Human-readable name for a virtual-key code. Never throws.
    /// </summary>
    public static string KeyName(int vk)
    {
        switch (vk)
        {
            case 0x1B: return "Esc";
            case 0x20: return "Space";
            case 0x09: return "Tab";
            case 0x0D: return "Enter";
            case 0x08: return "Backspace";
            case 0x2E: return "Delete";
            case 0x25: return "Left";
            case 0x26: return "Up";
            case 0x27: return "Right";
            case 0x28: return "Down";
            case 0x5B: return "Win";
            default:
                if (vk >= 0x41 && vk <= 0x5A)
                    return ((char)vk).ToString();
                if (vk >= 0x70 && vk <= 0x87)
                    return $"F{vk - 0x70 + 1}";
                return $"0x{vk:X2}";
        }
    }
}
