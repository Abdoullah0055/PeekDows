using System;
using System.Collections.Generic;

namespace PeekDows.App.Settings;

/// <summary>Parse/format "Ctrl+Alt+X". Pur. Modificateurs : Ctrl, Alt, Shift, Win.</summary>
public static class HotkeyParser
{
    private static readonly Dictionary<string, uint> Mods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = 0x0002u, ["Control"] = 0x0002u,
        ["Alt"] = 0x0001u,
        ["Shift"] = 0x0004u,
        ["Win"] = 0x0008u, ["Windows"] = 0x0008u,
    };

    private static readonly Dictionary<string, uint> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20u, ["P"] = 0x50u, ["O"] = 0x4Fu,
        ["A"] = 0x41u, ["B"] = 0x42u, ["C"] = 0x43u, ["D"] = 0x44u,
        ["E"] = 0x45u, ["F"] = 0x46u, ["G"] = 0x47u, ["H"] = 0x48u,
        ["I"] = 0x49u, ["J"] = 0x4Au, ["K"] = 0x4Bu, ["L"] = 0x4Cu,
        ["M"] = 0x4Du, ["N"] = 0x4Eu, ["Q"] = 0x51u, ["R"] = 0x52u,
        ["S"] = 0x53u, ["T"] = 0x54u, ["U"] = 0x55u, ["V"] = 0x56u,
        ["W"] = 0x57u, ["X"] = 0x58u, ["Y"] = 0x59u, ["Z"] = 0x5Au,
    };

    public static bool TryParse(string? gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        uint mod = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!Mods.TryGetValue(parts[i], out var m)) return false;
            mod |= m;
        }
        var last = parts[^1];
        if (last.Length == 1 && char.IsDigit(last[0]))
        {
            vk = (uint)(0x30 + (last[0] - '0'));
        }
        else if (!Keys.TryGetValue(last, out vk))
        {
            if (last.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(last[1..], out var f) && f is >= 1 and <= 12)
                vk = (uint)(0x70 + f - 1);
            else return false;
        }
        if (mod == 0) return false;
        modifiers = mod;
        return true;
    }

    public static string Format(uint modifiers, uint vk)
    {
        var parts = new List<string>(4);
        if ((modifiers & 0x0002u) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001u) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004u) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008u) != 0) parts.Add("Win");
        foreach (var kv in Keys) if (kv.Value == vk) { parts.Add(kv.Key); return string.Join("+", parts); }
        if (vk is >= 0x30 and <= 0x39) parts.Add(((char)vk).ToString());
        else if (vk is >= 0x70 and <= 0x7B) parts.Add("F" + (vk - 0x70 + 1));
        else parts.Add($"VK{vk:X}");
        return string.Join("+", parts);
    }
}
