using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Agent F3 — AppRuleService (pur, sans IO) :
/// pré-tri pin-first / ignore / matching insensible à la casse (+".exe") /
/// règles invalides ignorées / post-swap des TargetRect / listes vides.
/// </summary>
public sealed class AppRuleServiceTests
{
    private static ManagedWindow Win(int hwnd, string process) => new()
    {
        Hwnd = (IntPtr)hwnd,
        ProcessName = process,
        Title = process,
        FirstSeenAt = DateTime.Now
    };

    private static AppRule Pin(string process, string slot) => new() { Process = process, Action = "pin", Slot = slot };
    private static AppRule Ignore(string process) => new() { Process = process, Action = "ignore", Slot = "" };

    private static WindowPlacement Place(int hwnd, string slot, Rect rect) => new()
    {
        Hwnd = (IntPtr)hwnd,
        SlotId = slot,
        TargetRect = rect
    };

    private static readonly Rect RectA = new(0, 0, 100, 100);
    private static readonly Rect RectB = new(200, 0, 100, 100);
    private static readonly Rect RectC = new(0, 200, 100, 100);

    // ----- SortForArrange -----

    [Fact]
    public void SortForArrange_PinFirst_RespectsRuleOrder()
    {
        var arrangeable = new List<ManagedWindow> { Win(1, "chrome.exe"), Win(2, "notepad.exe"), Win(3, "calc.exe") };
        var rules = new List<AppRule> { Pin("calc", "A"), Pin("notepad", "B") };

        var sorted = AppRuleService.SortForArrange(arrangeable, rules).ToList();

        Assert.Equal(new[] { (IntPtr)3, (IntPtr)2, (IntPtr)1 }, sorted.Select(w => w.Hwnd));
    }

    [Fact]
    public void SortForArrange_Ignore_Removed()
    {
        var arrangeable = new List<ManagedWindow> { Win(1, "chrome.exe"), Win(2, "notepad.exe") };
        var rules = new List<AppRule> { Ignore("chrome.exe") };

        var sorted = AppRuleService.SortForArrange(arrangeable, rules).ToList();

        Assert.Single(sorted);
        Assert.Equal((IntPtr)2, sorted[0].Hwnd);
    }

    [Fact]
    public void SortForArrange_Matching_IsCaseInsensitive()
    {
        var arrangeable = new List<ManagedWindow> { Win(1, "chrome.exe"), Win(2, "NOTEPAD.EXE") };
        var rules = new List<AppRule> { Pin("NotePad", "A") };

        var sorted = AppRuleService.SortForArrange(arrangeable, rules).ToList();

        Assert.Equal((IntPtr)2, sorted[0].Hwnd);
    }

    [Fact]
    public void SortForArrange_Matching_StripsExeBothSides()
    {
        // Fenêtre stockée AVEC ".exe" (format WindowDiscoveryService), règle SANS.
        var withExe = new List<ManagedWindow> { Win(1, "chrome"), Win(2, "notepad.exe") };
        var sorted1 = AppRuleService.SortForArrange(withExe, new List<AppRule> { Pin("notepad", "A") }).ToList();
        Assert.Equal((IntPtr)2, sorted1[0].Hwnd);

        // Fenêtre SANS ".exe", règle AVEC.
        var withoutExe = new List<ManagedWindow> { Win(1, "chrome"), Win(2, "notepad") };
        var sorted2 = AppRuleService.SortForArrange(withoutExe, new List<AppRule> { Pin("NOTEPAD.EXE", "A") }).ToList();
        Assert.Equal((IntPtr)2, sorted2[0].Hwnd);
    }

    [Fact]
    public void SortForArrange_InvalidRules_Ignored()
    {
        var arrangeable = new List<ManagedWindow> { Win(1, "a.exe"), Win(2, "b.exe") };
        var rules = new List<AppRule>
        {
            new() { Process = "", Action = "pin", Slot = "A" },       // process vide
            new() { Process = "   ", Action = "ignore", Slot = "" },  // process blanc
            new() { Process = "a.exe", Action = "float", Slot = "A" },// action inconnue
            new() { Process = "a.exe", Action = "pin", Slot = "Z" },  // slot hors A-H
            new() { Process = "a.exe", Action = "pin", Slot = "" },   // slot manquant pour pin
            new() { Process = ".exe", Action = "pin", Slot = "A" },   // que l'extension
        };

        var sorted = AppRuleService.SortForArrange(arrangeable, rules).ToList();

        Assert.Equal(new[] { (IntPtr)1, (IntPtr)2 }, sorted.Select(w => w.Hwnd));
    }

    [Fact]
    public void SortForArrange_EmptyRules_ReturnsSameOrderAsCopy()
    {
        var arrangeable = new List<ManagedWindow> { Win(1, "b.exe"), Win(2, "a.exe") };

        var sorted = AppRuleService.SortForArrange(arrangeable, new List<AppRule>()).ToList();

        Assert.Equal(new[] { (IntPtr)1, (IntPtr)2 }, sorted.Select(w => w.Hwnd));
        Assert.NotSame(arrangeable, sorted);
    }

    [Fact]
    public void SortForArrange_EmptyArrangeable_ReturnsEmpty()
    {
        var sorted = AppRuleService.SortForArrange(new List<ManagedWindow>(), new List<AppRule> { Pin("a", "A") });

        Assert.Empty(sorted);
    }

    [Fact]
    public void SortForArrange_IgnoreBeatsPin_ForSameProcess()
    {
        var arrangeable = new List<ManagedWindow> { Win(1, "notepad.exe"), Win(2, "chrome.exe") };
        var rules = new List<AppRule> { Pin("notepad", "A"), Ignore("NOTEPAD.exe") };

        var sorted = AppRuleService.SortForArrange(arrangeable, rules).ToList();

        Assert.Single(sorted);
        Assert.Equal((IntPtr)2, sorted[0].Hwnd);
    }

    [Fact]
    public void SortForArrange_DuplicatePinRule_FirstWins_WindowAppearsOnce()
    {
        var arrangeable = new List<ManagedWindow> { Win(1, "notepad.exe"), Win(2, "chrome.exe") };
        var rules = new List<AppRule> { Pin("notepad", "A"), Pin("Notepad.exe", "B") };

        var sorted = AppRuleService.SortForArrange(arrangeable, rules).ToList();

        Assert.Equal(2, sorted.Count);
        Assert.Equal((IntPtr)1, sorted[0].Hwnd);
        Assert.Single(sorted, w => w.Hwnd == (IntPtr)1);
    }

    [Fact]
    public void SortForArrange_RestKeepsOriginalOrder()
    {
        var arrangeable = new List<ManagedWindow>
        {
            Win(1, "c.exe"), Win(2, "pinned.exe"), Win(3, "a.exe"), Win(4, "b.exe")
        };
        var rules = new List<AppRule> { Pin("pinned", "A") };

        var sorted = AppRuleService.SortForArrange(arrangeable, rules).ToList();

        Assert.Equal(new[] { (IntPtr)2, (IntPtr)1, (IntPtr)3, (IntPtr)4 }, sorted.Select(w => w.Hwnd));
    }

    // ----- ApplyPinnedSlots -----

    [Fact]
    public void ApplyPinnedSlots_TwoPins_ExchangeRects()
    {
        var placements = new List<WindowPlacement>
        {
            Place(1, "A", RectA),
            Place(2, "B", RectB),
        };
        var byHwnd = new Dictionary<IntPtr, string> { [(IntPtr)1] = "alpha.exe", [(IntPtr)2] = "beta.exe" };
        var rules = new List<AppRule> { Pin("alpha", "B"), Pin("beta", "A") };

        AppRuleService.ApplyPinnedSlots(placements, byHwnd, rules);

        Assert.Equal(RectB, placements[0].TargetRect);
        Assert.Equal(RectA, placements[1].TargetRect);
        // Contrat : seuls les TargetRect sont échangés, les SlotId sont conservés.
        Assert.Equal("A", placements[0].SlotId);
        Assert.Equal("B", placements[1].SlotId);
    }

    [Fact]
    public void ApplyPinnedSlots_MissingSlot_Unchanged()
    {
        var placements = new List<WindowPlacement> { Place(1, "A", RectA), Place(2, "B", RectB) };
        var byHwnd = new Dictionary<IntPtr, string> { [(IntPtr)1] = "alpha.exe", [(IntPtr)2] = "beta.exe" };
        // Slot H valide mais absent des placements (moins de fenêtres) → ne fait rien.
        var rules = new List<AppRule> { Pin("alpha", "H") };

        AppRuleService.ApplyPinnedSlots(placements, byHwnd, rules);

        Assert.Equal(RectA, placements[0].TargetRect);
        Assert.Equal(RectB, placements[1].TargetRect);
    }

    [Fact]
    public void ApplyPinnedSlots_MissingHwnd_Unchanged()
    {
        var placements = new List<WindowPlacement> { Place(1, "A", RectA), Place(2, "B", RectB) };
        var byHwnd = new Dictionary<IntPtr, string> { [(IntPtr)1] = "alpha.exe", [(IntPtr)2] = "beta.exe" };
        var rules = new List<AppRule> { Pin("ghost", "A") };

        AppRuleService.ApplyPinnedSlots(placements, byHwnd, rules);

        Assert.Equal(RectA, placements[0].TargetRect);
        Assert.Equal(RectB, placements[1].TargetRect);
    }

    [Fact]
    public void ApplyPinnedSlots_AlreadySeated_Unchanged()
    {
        var placements = new List<WindowPlacement> { Place(1, "A", RectA), Place(2, "B", RectB) };
        var byHwnd = new Dictionary<IntPtr, string> { [(IntPtr)1] = "alpha.exe", [(IntPtr)2] = "beta.exe" };
        var rules = new List<AppRule> { Pin("alpha.exe", "A") };

        AppRuleService.ApplyPinnedSlots(placements, byHwnd, rules);

        Assert.Equal(RectA, placements[0].TargetRect);
        Assert.Equal(RectB, placements[1].TargetRect);
    }

    [Fact]
    public void ApplyPinnedSlots_SameSlotConflict_FirstWins()
    {
        var placements = new List<WindowPlacement>
        {
            Place(1, "A", RectA),
            Place(2, "B", RectB),
            Place(3, "C", RectC),
        };
        var byHwnd = new Dictionary<IntPtr, string>
        {
            [(IntPtr)1] = "alpha.exe", [(IntPtr)2] = "beta.exe", [(IntPtr)3] = "gamma.exe"
        };
        // alpha déjà en A ; beta réclame aussi A → premier arrivé gagne, rien ne bouge.
        var rules = new List<AppRule> { Pin("alpha", "A"), Pin("beta", "A") };

        AppRuleService.ApplyPinnedSlots(placements, byHwnd, rules);

        Assert.Equal(RectA, placements[0].TargetRect);
        Assert.Equal(RectB, placements[1].TargetRect);
        Assert.Equal(RectC, placements[2].TargetRect);
    }

    [Fact]
    public void ApplyPinnedSlots_InvalidRules_Ignored()
    {
        var placements = new List<WindowPlacement> { Place(1, "A", RectA), Place(2, "B", RectB) };
        var byHwnd = new Dictionary<IntPtr, string> { [(IntPtr)1] = "alpha.exe", [(IntPtr)2] = "beta.exe" };
        var rules = new List<AppRule>
        {
            new() { Process = "", Action = "pin", Slot = "B" },
            new() { Process = "alpha", Action = "float", Slot = "B" },
            new() { Process = "alpha", Action = "pin", Slot = "Z" },
            Ignore("beta"), // les règles ignore ne participent pas au post-swap
        };

        AppRuleService.ApplyPinnedSlots(placements, byHwnd, rules);

        Assert.Equal(RectA, placements[0].TargetRect);
        Assert.Equal(RectB, placements[1].TargetRect);
    }

    [Fact]
    public void ApplyPinnedSlots_EmptyInputs_NoThrowAndUnchanged()
    {
        var placements = new List<WindowPlacement> { Place(1, "A", RectA) };
        var byHwnd = new Dictionary<IntPtr, string> { [(IntPtr)1] = "alpha.exe" };

        var ex1 = Record.Exception(() => AppRuleService.ApplyPinnedSlots(placements, byHwnd, new List<AppRule>()));
        var ex2 = Record.Exception(() => AppRuleService.ApplyPinnedSlots(
            new List<WindowPlacement>(), byHwnd, new List<AppRule> { Pin("alpha", "A") }));

        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.Equal(RectA, placements[0].TargetRect);
    }

    // ----- validation partagée (réutilisée par le bridge S2) -----

    [Fact]
    public void CleanRules_DropsInvalid_DedupsFirstWins_Normalizes()
    {
        var input = new List<AppRule>
        {
            Pin("  Notepad.EXE ", "b"),
            new() { Process = "", Action = "pin", Slot = "A" },
            new() { Process = "calc", Action = "nope", Slot = "A" },
            Pin("NOTEPAD", "C"), // doublon : premier gagne
            Ignore("GameBAR"),
        };

        var clean = AppRuleService.CleanRules(input);

        Assert.Equal(2, clean.Count);
        Assert.Equal("Notepad.EXE", clean[0].Process);
        Assert.Equal("pin", clean[0].Action);
        Assert.Equal("B", clean[0].Slot);
        Assert.Equal("ignore", clean[1].Action);
    }
}
