using System;
using System.Collections.Generic;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

/// <summary>
/// Agent F3 — per-app arrange rules (pin a process to a slot, or ignore it).
/// Pure and testable: ZÉRO géométrie, no IO, no logging. Never throws — every
/// entry point guards against null inputs and swallows unexpected exceptions
/// with a best-effort fallback.
/// Protocol (2 temps, piloté par l'intégrateur dans ExecuteArrangeCore) :
/// (1) pré-tri via <see cref="SortForArrange"/> — épinglées d'abord dans l'ordre
/// des règles, puis le reste INCHANGÉ dans son ordre, ignorées retirées ;
/// (2) post-swap Grid via <see cref="ApplyPinnedSlots"/> — échange les TargetRect
/// pour asseoir chaque épinglée sur son slot exact.
/// Process matching : OrdinalIgnoreCase, ".exe" strippé des deux côtés
/// (WindowDiscoveryService stocke "notepad.exe", les règles peuvent omettre ".exe").
/// </summary>
public static class AppRuleService
{
    /// <summary>Trim + strip one trailing ".exe" (case-insensitive). "" when empty.</summary>
    public static string NormalizeProcess(string? process)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(process))
                return string.Empty;
            var p = process.Trim();
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                p = p.Substring(0, p.Length - 4).Trim();
            return p;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool IsPinAction(string? action)
        => string.Equals(action?.Trim(), "pin", StringComparison.OrdinalIgnoreCase);

    public static bool IsIgnoreAction(string? action)
        => string.Equals(action?.Trim(), "ignore", StringComparison.OrdinalIgnoreCase);

    /// <summary>Single char A..H (case-insensitive, surrounding whitespace allowed).</summary>
    public static bool IsValidSlot(string? slot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slot))
                return false;
            var s = slot.Trim();
            if (s.Length != 1)
                return false;
            char c = char.ToUpperInvariant(s[0]);
            return c >= 'A' && c <= 'H';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A rule is valid when: process non-empty (after ".exe" strip),
    /// action pin|ignore (case-insensitive), and — for pin only — slot A..H.
    /// Ignore rules accept any or missing slot.
    /// </summary>
    public static bool IsValidRule(AppRule? rule)
    {
        try
        {
            if (rule is null)
                return false;
            if (NormalizeProcess(rule.Process).Length == 0)
                return false;
            if (IsPinAction(rule.Action))
                return IsValidSlot(rule.Slot);
            if (IsIgnoreAction(rule.Action))
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Canonical copy (trimmed process, lowercase action, uppercase pin slot),
    /// or null when the rule is invalid. Used bridge-side on apply so dirty
    /// tracking compares canonical values.
    /// </summary>
    public static AppRule? NormalizeRule(AppRule? rule)
    {
        try
        {
            if (!IsValidRule(rule) || rule is null)
                return null;
            var action = IsIgnoreAction(rule.Action) ? "ignore" : "pin";
            var slot = rule.Slot?.Trim() ?? string.Empty;
            if (action == "pin")
                slot = slot.ToUpperInvariant();
            return new AppRule
            {
                Process = rule.Process.Trim(),
                Action = action,
                Slot = slot
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Drops invalid entries, dedups by normalized process (first wins,
    /// case-insensitive). Used bridge-side on apply (S2).
    /// </summary>
    public static List<AppRule> CleanRules(IEnumerable<AppRule>? input)
    {
        var clean = new List<AppRule>();
        try
        {
            if (input is null)
                return clean;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in input)
            {
                var n = NormalizeRule(rule);
                if (n is null)
                    continue;
                if (!seen.Add(NormalizeProcess(n.Process)))
                    continue;
                clean.Add(n);
            }
        }
        catch
        {
            // best-effort: return what was collected so far
        }
        return clean;
    }

    /// <summary>
    /// Canonical equality for dirty tracking (bridge-side: same rules → no notify).
    /// Process/action compare case-insensitively; slot only matters for pin rules.
    /// </summary>
    public static bool AreRulesEqual(IReadOnlyList<AppRule>? a, IReadOnlyList<AppRule>? b)
    {
        try
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a is null || b is null)
                return false;
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                var x = a[i];
                var y = b[i];
                if (x is null || y is null)
                    return false;
                if (!string.Equals(NormalizeProcess(x.Process), NormalizeProcess(y.Process), StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(x.Action?.Trim(), y.Action?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
                if (IsPinAction(x.Action) || IsPinAction(y.Action))
                {
                    if (!string.Equals(x.Slot?.Trim(), y.Slot?.Trim(), StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// (1) PRÉ-TRI — réordonne la liste arrangeable : épinglées valides D'ABORD
    /// dans l'ordre des règles (première fenêtre correspondante par règle),
    /// puis le reste INCHANGÉ dans son ordre, ignorées RETIRÉES.
    /// Règle invalide → ignorée. Ignore prioritaire sur tout : un process à la
    /// fois épinglé et ignoré est exclu. Ne mute jamais l'entrée ; jamais de throw.
    /// </summary>
    public static IReadOnlyList<ManagedWindow> SortForArrange(
        IReadOnlyList<ManagedWindow> arrangeable,
        IReadOnlyList<AppRule> rules)
    {
        try
        {
            if (arrangeable is null || arrangeable.Count == 0)
                return new List<ManagedWindow>();
            if (rules is null || rules.Count == 0)
                return new List<ManagedWindow>(arrangeable);

            var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pins = new List<(string Process, string Slot)>();
            var pinSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                if (!IsValidRule(rule) || rule is null)
                    continue;
                var norm = NormalizeProcess(rule.Process);
                if (IsIgnoreAction(rule.Action))
                {
                    ignore.Add(norm);
                    continue;
                }
                if (IsPinAction(rule.Action))
                {
                    // Premier arrivé gagne par process (le pré-tri est déterministe).
                    if (pinSeen.Add(norm))
                        pins.Add((norm, rule.Slot.Trim().ToUpperInvariant()));
                }
            }

            // Ignore prioritaire sur tout.
            if (pins.Count > 0 && ignore.Count > 0)
                pins.RemoveAll(p => ignore.Contains(p.Process));

            var used = new bool[arrangeable.Count];
            var result = new List<ManagedWindow>(arrangeable.Count);

            // (a) épinglées d'abord, dans l'ordre des règles.
            foreach (var pin in pins)
            {
                for (int i = 0; i < arrangeable.Count; i++)
                {
                    if (used[i])
                        continue;
                    var w = arrangeable[i];
                    if (w is null)
                        continue;
                    if (string.Equals(NormalizeProcess(w.ProcessName), pin.Process, StringComparison.OrdinalIgnoreCase))
                    {
                        used[i] = true;
                        result.Add(w);
                        break;
                    }
                }
            }

            // (b) le reste INCHANGÉ dans son ordre, ignorées exclues.
            for (int i = 0; i < arrangeable.Count; i++)
            {
                if (used[i])
                    continue;
                var w = arrangeable[i];
                if (w is null)
                    continue;
                if (ignore.Contains(NormalizeProcess(w.ProcessName)))
                    continue;
                result.Add(w);
            }

            return result;
        }
        catch
        {
            try
            {
                return arrangeable is null
                    ? new List<ManagedWindow>()
                    : new List<ManagedWindow>(arrangeable);
            }
            catch
            {
                return new List<ManagedWindow>();
            }
        }
    }

    /// <summary>
    /// (2) POST-SWAP Grid — pour chaque règle pin valide (dans l'ordre), trouve le
    /// placement de la fenêtre correspondante (via <paramref name="processByHwnd"/>,
    /// construite par l'intégrateur depuis arrangeable) et celui qui occupe déjà le
    /// slot : ÉCHANGE leurs TargetRect (SlotId conservés, cf. contrat). Si le slot
    /// cible n'existe dans aucun placement, ou si le hwnd est absent : ne fait rien.
    /// Conflit 2 apps → même slot : premier arrivé gagne (slots déjà réclamés sautés).
    /// Ne mute que par remplacement d'entrées (WindowPlacement est init-only).
    /// Jamais de throw.
    /// </summary>
    public static void ApplyPinnedSlots(
        IList<WindowPlacement> placements,
        IReadOnlyDictionary<IntPtr, string> processByHwnd,
        IReadOnlyList<AppRule> rules)
    {
        try
        {
            if (placements is null || placements.Count == 0)
                return;
            if (placements.IsReadOnly)
                return;
            if (processByHwnd is null || processByHwnd.Count == 0)
                return;
            if (rules is null || rules.Count == 0)
                return;

            var pins = new List<(string Process, string Slot)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                if (!IsValidRule(rule) || rule is null)
                    continue;
                if (!IsPinAction(rule.Action))
                    continue;
                var norm = NormalizeProcess(rule.Process);
                if (seen.Add(norm))
                    pins.Add((norm, rule.Slot.Trim().ToUpperInvariant()));
            }
            if (pins.Count == 0)
                return;

            // Référence slot → rect depuis les placements INITIAUX (premier gagne).
            var slotRect = new Dictionary<string, Rect>(StringComparer.Ordinal);
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                if (p is null || string.IsNullOrEmpty(p.SlotId))
                    continue;
                if (!slotRect.ContainsKey(p.SlotId))
                    slotRect[p.SlotId] = p.TargetRect;
            }

            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pin in pins)
            {
                // Conflit même slot : premier arrivé gagne.
                if (claimed.Contains(pin.Slot))
                    continue;
                // Slot vide/inexistant : ne fait rien (le pré-tri a déjà bien positionné).
                if (!slotRect.TryGetValue(pin.Slot, out var desired))
                    continue;

                // Placement de la fenêtre épinglée (première correspondante).
                int pinnedIdx = -1;
                for (int i = 0; i < placements.Count; i++)
                {
                    var p = placements[i];
                    if (p is null)
                        continue;
                    if (!processByHwnd.TryGetValue(p.Hwnd, out var proc))
                        continue;
                    if (string.Equals(NormalizeProcess(proc), pin.Process, StringComparison.OrdinalIgnoreCase))
                    {
                        pinnedIdx = i;
                        break;
                    }
                }
                if (pinnedIdx < 0)
                    continue; // hwnd absent : ne fait rien.

                var pinned = placements[pinnedIdx]!;
                if (pinned.TargetRect.Equals(desired))
                {
                    claimed.Add(pin.Slot);
                    continue; // déjà assise sur son slot.
                }

                // Occupant actuel = celui qui DÉTIENT le rect désiré (comparaison par
                // rect, pas par SlotId : après un swap précédent les SlotId ne suivent
                // plus les rects, et ça évite le ping-pong sur pins croisés A↔B).
                int occupantIdx = -1;
                for (int j = 0; j < placements.Count; j++)
                {
                    if (j == pinnedIdx)
                        continue;
                    var q = placements[j];
                    if (q is null)
                        continue;
                    if (q.TargetRect.Equals(desired))
                    {
                        occupantIdx = j;
                        break;
                    }
                }

                if (occupantIdx >= 0)
                {
                    var occupant = placements[occupantIdx]!;
                    var pinnedRect = pinned.TargetRect;
                    placements[pinnedIdx] = WithRect(pinned, desired);
                    placements[occupantIdx] = WithRect(occupant, pinnedRect);
                }
                else
                {
                    placements[pinnedIdx] = WithRect(pinned, desired);
                }
                claimed.Add(pin.Slot);
            }
        }
        catch
        {
            // Jamais de throw.
        }
    }

    private static WindowPlacement WithRect(WindowPlacement placement, Rect rect)
    {
        return new WindowPlacement
        {
            Hwnd = placement.Hwnd,
            SlotId = placement.SlotId,
            TargetRect = rect,
            Activate = placement.Activate,
            PreserveZOrder = placement.PreserveZOrder,
            BringToFront = placement.BringToFront,
            Kind = placement.Kind
        };
    }
}
