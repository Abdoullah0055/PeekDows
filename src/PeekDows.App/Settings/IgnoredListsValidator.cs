using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekDows.App.Settings;

/// <summary>Nettoyage des listes d'ignorés : trim, vide→drop, dedup insensible à la casse. Pur, testable.</summary>
public static class IgnoredListsValidator
{
    public static List<string> Clean(IEnumerable<string>? input)
    {
        if (input is null) return new List<string>();
        return input.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
