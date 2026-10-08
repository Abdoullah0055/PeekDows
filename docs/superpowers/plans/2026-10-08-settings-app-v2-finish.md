# Settings App v2 Finish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Activer les 3 pages `Windows (lecture-seule) / Hotkeys (arrangeNow + pauseResume) / Ignored apps` dans la Settings WebView2 avec le même modèle draft-avec-Save.

**Architecture:** `Task 0` convertit `SettingsBridge`, `SettingsDraft`, `HotkeyService`, `PeekDowsAppContext` en `partial` et déclare des hooks disjoints (`AugmentSnapshotIgnored/Hotkeys`, `ApplyIgnoredDraft/ApplyHotkeysDraft`, `TryHandleWindowsMessage`). Ensuite `A+B+C` codent en parallèle dans des fichiers partiels exclusifs (zéro chevauchement), `D` intègre l'UI shell + docs + gates. Aucun secret/PII/chemin local n'est committé (garde open-source par commit).

**Tech Stack:** WinForms + WebView2 (`SettingsHostForm`), JS vanilla (`wwwroot/app.js`), `NativeMethods.RegisterHotKey`, xUnit, `dotnet build/test` (.NET 10 SDK).

---

## Parallel Execution Contract (lire avant de dispatcher)

**Ordre :** `Task 0` (inline, 1 commit) PUIS `Task A + Task B + Task C` en parallèle (3 subagents, fichiers disjoints, aucun rebase nécessaire) PUIS `Task D` (1 subagent, après A+B+C).

**File ownership — aucun fichier n'est partagé entre A/B/C :**

| Owner | Fichiers EXCLUSIFS (créés, jamais touchés par un autre) |
|---|---|
| Task 0 | Modifie 4 lignes `partial` + crée `WindowRow.cs` + ajoute 2 signatures à `IPeekDowsController.cs` |
| Agent A (Ignored) | `src/PeekDows.App/Settings/SettingsDraft.Ignored.cs`, `SettingsBridge.Ignored.cs`, `IgnoredListsValidator.cs`, `tests/PeekDows.Tests/SettingsBridgeIgnoredTests.cs`, `docs/snippets/ignored-ui.md` |
| Agent B (Hotkeys) | `src/PeekDows.App/Settings/HotkeyParser.cs`, `src/PeekDows.App/Settings/SettingsDraft.Hotkeys.cs`, `src/PeekDows.App/Settings/SettingsBridge.Hotkeys.cs`, `src/PeekDows.App/Hotkeys/HotkeyService.TryUpdate.cs`, `src/PeekDows.App/PeekDowsAppContext.Hotkeys.cs`, `tests/PeekDows.Tests/HotkeyParserTests.cs`, `tests/PeekDows.Tests/SettingsBridgeHotkeysTests.cs`, `docs/snippets/hotkeys-ui.md` |
| Agent C (Windows) | `src/PeekDows.Core/Models/WindowRow.cs` (si non créé en Task 0 — sinon跳过, voir note), `src/PeekDows.App/Settings/WindowsSnapshotProvider.cs`, `src/PeekDows.App/Settings/SettingsBridge.Windows.cs`, `src/PeekDows.App/PeekDowsAppContext.Windows.cs`, `tests/PeekDows.Tests/SettingsBridgeWindowsTests.cs`, `docs/snippets/windows-ui.md` |
| Agent D (Shell) | `src/PeekDows.App/wwwroot/index.html`, `app.js`, `app.css`, `src/PeekDows.App/Settings/SettingsHostForm.cs`, `docs/MANUAL_TEST_CHECKLIST.md`, `README.md` |

**Règles :**
- A/B/C ne modifient JAMAIS `index.html`, `app.js`, `app.css`, `PeekDowsAppContext.cs` (core), `SettingsHostForm.cs`, `IPeekDowsController.cs`, `README.md`. Ils livrent leurs snippets UI dans `docs/snippets/*.md` que D injecte.
- A/B/C ne modifient JAMAIS `tests/PeekDows.Tests/SettingsBridgeTests.cs` (fichier partagé existant). Chacun crée son propre fichier de tests.
- `git add` uniquement les fichiers de la colonne owner. Vérifier par `git status --short` avant chaque commit.

**Garde open-source (chaque commit) :**

```powershell
git status --short
git diff --check
Select-String -Path "src/*","tests/*" -Pattern "PASSWORD|SECRET|TOKEN|BEGIN.*PRIVATE|ghp_|gho_|sk-live|@gmail\.com|C:\\Users\\" | Select-Object Path,LineNumber
```

Expected : `git diff --check` vide, `Select-String` aucune sortie, `git status` uniquement les fichiers owner.

---

## Task 0: Prep — partials + contrats (PRÉREQUIS, inline avant parallélisme)

**Files:**
- Modify: `src/PeekDows.App/Settings/SettingsBridge.cs:13` (`SettingsDraft` → partial, extrait vers fichier dédié si besoin)
- Modify: `src/PeekDows.App/Settings/SettingsBridge.cs:37` (`SettingsBridge` → partial + hooks)
- Modify: `src/PeekDows.App/Hotkeys/HotkeyService.cs:9` (`HotkeyService` → partial)
- Modify: `src/PeekDows.App/PeekDowsAppContext.cs:17` (`PeekDowsAppContext` → partial)
- Modify: `src/PeekDows.App/Tray/IPeekDowsController.cs` (ajout 2 signatures)
- Create: `src/PeekDows.App/Settings/SettingsDraft.cs`
- Create: `src/PeekDows.App/Settings/WindowRow.cs`

- [ ] **Step 1: Extraire `SettingsDraft` en fichier partial dédié**

État actuel (`SettingsBridge.cs:13-25`) :

```csharp
public sealed class SettingsDraft
{
    public bool? Enabled { get; set; }
    // ... 9 autres props
}
```

Créer `src/PeekDows.App/Settings/SettingsDraft.cs` :

```csharp
namespace PeekDows.App.Settings;

/// <summary>Draft éditable envoyé par l'UI sur Save. Clés nullables = "untouched".</summary>
public sealed partial class SettingsDraft
{
    public bool? Enabled { get; set; }
    public bool? AutoArrange { get; set; }
    public bool? Animate { get; set; }
    public bool? DirectionalFocus { get; set; }
    public bool? StartWithWindows { get; set; }
    public bool? AllowRepositionMaximized { get; set; }
    public string? Preset { get; set; }
    public bool? ArrangeOnStartup { get; set; }
    public bool? ShowTrayNotifications { get; set; }
    public int? ThresholdPx { get; set; }
}
```

Supprimer la classe de `SettingsBridge.cs` (garder uniquement `SettingsBridge`).

- [ ] **Step 2: Rendre `SettingsBridge` partial + hooks disjoints**

Dans `SettingsBridge.cs`, changer :

```csharp
public sealed class SettingsBridge
```

en :

```csharp
public sealed partial class SettingsBridge
```

Ajouter à la fin de la classe (avant l'accolade fermante) :

```csharp
// ----- hooks v2 (implémentés dans des fichiers partiels exclusifs) -----
// Note: les hooks void sans implémentation sont des no-ops (pas d'erreur build).
// Pour Windows on utilise un handler injectable (un partial non-void exigerait
// une implémentation immédiate et casserait le build avant l'arrivée de l'Agent C).
partial void AugmentSnapshotIgnored(System.Collections.Generic.Dictionary<string, object?> data);
partial void AugmentSnapshotHotkeys(System.Collections.Generic.Dictionary<string, object?> data);
partial void ApplyIgnoredDraft(SettingsDraft draft, Core.Models.AppSettings settings, ref bool plainChanged);
partial void ApplyHotkeysDraft(SettingsDraft draft, ref string? hotkeyError);

private Func<string, JsonElement, string?>? _windowsMessageHandler;

/// <summary>Enregistre le handler du message requestWindows (fourni par l'Agent C, wiré par D).</summary>
public void SetWindowsMessageHandler(Func<string, JsonElement, string?> handler)
    => _windowsMessageHandler = handler;

private string? TryHandleWindowsMessage(string type, JsonElement root)
    => _windowsMessageHandler?.Invoke(type, root);
```

Modifier `SnapshotData()` pour fusionner (remplacer le `return new {...}` anonyme par dict + hooks) :

```csharp
private object SnapshotData()
{
    var s = _controller.CurrentSettings;
    var data = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["enabled"] = s.Enabled,
        ["autoArrange"] = s.AutoArrange,
        ["animate"] = s.AnimateWindowTransitions,
        ["directionalFocus"] = s.DirectionalFocusEnabled,
        ["startWithWindows"] = s.StartWithWindows,
        ["allowRepositionMaximized"] = s.AllowRepositionMaximizedWindows,
        ["preset"] = s.WindowSizePreset.ToString(),
        ["arrangeOnStartup"] = s.ArrangeOnStartup,
        ["showTrayNotifications"] = s.ShowTrayNotifications,
        ["thresholdPx"] = s.DirectionalFocusThresholdPx,
        ["version"] = s.Version,
        ["appVersion"] = _appVersionProvider(),
    };
    AugmentSnapshotIgnored(data);
    AugmentSnapshotHotkeys(data);
    return data;
}
```

Modifier `ApplyDraft` : après le bloc `plainChanged` existant et avant `return Applied(...)`, insérer :

```csharp
ApplyIgnoredDraft(draft, settings, ref plainChanged);
string? hotkeyError = null;
ApplyHotkeysDraft(draft, ref hotkeyError);
if (hotkeyError is not null)
    return Applied(ok: false, error: hotkeyError);
if (plainChanged)
{
    _settingsService.Save(settings);
    _controller.OnSettingsChanged();
}
```

Note : le bloc `if (plainChanged)` existant doit être déplacé APRÈS les deux appels (un seul `Save`, voir code final en Task 0 commit). Si le fichier actuel fait déjà `Save` avant, le déplacer après.

Modifier `HandleMessage` : dans le `switch (type)`, avant `default:`, ajouter :

```csharp
case "requestWindows":
    var windowsResponse = TryHandleWindowsMessage(type, root);
    if (windowsResponse is not null) return windowsResponse;
    return Error("windows-provider-missing");
```

Et dans `default:` d'abord tenter :

```csharp
default:
    var ext = TryHandleWindowsMessage(type, root);
    if (ext is not null) return ext;
    _logger?.Info($"SettingsBridge: unknown message type '{type}' ignored");
    return Error($"unknown-type:{type}");
```

- [ ] **Step 3: Rendre `HotkeyService` et `PeekDowsAppContext` partial**

`HotkeyService.cs:9` : `public sealed class HotkeyService` → `public sealed partial class HotkeyService`.
`PeekDowsAppContext.cs:17` : `public class PeekDowsAppContext` → `public partial class PeekDowsAppContext`.

- [ ] **Step 4: Créer `WindowRow.cs` + étendre l'interface**

Créer `src/PeekDows.App/Settings/WindowRow.cs` :

```csharp
namespace PeekDows.App.Settings;

/// <summary>Ligne lecture-seule pour la page Windows v2. Aucune donnée perso.</summary>
public sealed record WindowRow(
    string Hwnd,
    string Title,
    string Process,
    string ClassName,
    bool Eligible,
    string Reason);
```

Dans `IPeekDowsController.cs`, ajouter (sans toucher aux membres existants) :

```csharp
bool TryUpdateHotkey(string name, string gesture, out string error);
System.Collections.Generic.IReadOnlyList<Settings.WindowRow> GetWindowsSnapshot();
```

- [ ] **Step 5: Build de vérification**

Run depuis `C:\Portfolio Prog\PeekDows` :

```powershell
dotnet build PeekDows.slnx
```

Expected : `0 Error(s)`, `Build succeeded.` (les hooks `partial void` sans implémentation sont des no-ops, les tests existants restent verts car le JSON sérialisé depuis un `Dictionary` garde les mêmes clés camelCase).

- [ ] **Step 6: Commit prep**

```bash
git add src/PeekDows.App/Settings/SettingsBridge.cs src/PeekDows.App/Settings/SettingsDraft.cs src/PeekDows.App/Settings/WindowRow.cs src/PeekDows.App/Hotkeys/HotkeyService.cs src/PeekDows.App/PeekDowsAppContext.cs src/PeekDows.App/Tray/IPeekDowsController.cs
git commit -m "refactor(settings): partial bridges and v2 hooks for parallel work"
```

Vérifier garde open-source (voir contrat ci-dessus). Ensuite seulement dispatcher A+B+C.

---

## Task A: Ignored Apps (Agent A — parallèle, fichiers exclusifs)

**Files (ne créer/modifier QUE ceux-ci) :**
- Create: `src/PeekDows.App/Settings/SettingsDraft.Ignored.cs`
- Create: `src/PeekDows.App/Settings/SettingsBridge.Ignored.cs`
- Create: `src/PeekDows.App/Settings/IgnoredListsValidator.cs`
- Create: `tests/PeekDows.Tests/SettingsBridgeIgnoredTests.cs`
- Create: `docs/snippets/ignored-ui.md`

Contexte : remplace `SettingsLegacyWindow.cs:102-118` (trim/dedup `OrdinalIgnoreCase`) dans le WebView2. `WindowClassifier.cs:23-45` définit les built-in (affichés lecture-seule, jamais éditables).

- [ ] **Step 1: Écrire le test qui échoue (validator pur)**

Créer `tests/PeekDows.Tests/SettingsBridgeIgnoredTests.cs` avec en tête :

```csharp
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Settings;
using Xunit;

namespace PeekDows.Tests;

public sealed class SettingsBridgeIgnoredTests
{
    [Fact]
    public void Snapshot_ContainsIgnoredArrays()
    {
        using var tmp = new TempSettingsScope();
        var bridge = new SettingsBridge(tmp.Controller, tmp.Service, appVersionProvider: () => "9.9.9");
        using var doc = JsonDocument.Parse(bridge.BuildSnapshotMessage());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.GetProperty("ignoredProcesses").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("ignoredClasses").ValueKind);
    }
}
```

Note : `TempSettingsScope` est un helper LOCAL à ce fichier (créer un dossier temp + `SettingsService(path)` + `FakeBridgeController` minimal copié depuis `SettingsBridgeTests.cs:300-396` — ne pas importer ni modifier le fichier partagé). Le test doit compiler mais ÉCHOUER (clés absentes → `KeyNotFoundException`).

- [ ] **Step 2: Run**

```powershell
dotnet test PeekDows.slnx --filter "Snapshot_ContainsIgnoredArrays"
```

Expected : FAIL.

- [ ] **Step 3: Implémenter le validator pur (aucune dépendance Win32)**

Créer `src/PeekDows.App/Settings/IgnoredListsValidator.cs` :

```csharp
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
```

Créer `src/PeekDows.App/Settings/SettingsDraft.Ignored.cs` :

```csharp
namespace PeekDows.App.Settings;

public sealed partial class SettingsDraft
{
    public System.Collections.Generic.List<string>? IgnoredProcesses { get; set; }
    public System.Collections.Generic.List<string>? IgnoredClasses { get; set; }
}
```

Créer `src/PeekDows.App/Settings/SettingsBridge.Ignored.cs` :

```csharp
using System.Collections.Generic;

namespace PeekDows.App.Settings;

public sealed partial class SettingsBridge
{
    partial void AugmentSnapshotIgnored(Dictionary<string, object?> data)
    {
        var s = _controller.CurrentSettings;
        data["ignoredProcesses"] = s.IgnoredProcesses ?? new List<string>();
        data["ignoredClasses"] = s.IgnoredClasses ?? new List<string>();
    }

    partial void ApplyIgnoredDraft(SettingsDraft draft, Core.Models.AppSettings settings, ref bool plainChanged)
    {
        if (draft.IgnoredProcesses is { } ip)
        {
            var clean = IgnoredListsValidator.Clean(ip);
            if (!clean.SequenceEqual(settings.IgnoredProcesses, System.StringComparer.OrdinalIgnoreCase))
            {
                settings.IgnoredProcesses.Clear();
                settings.IgnoredProcesses.AddRange(clean);
                plainChanged = true;
            }
        }
        if (draft.IgnoredClasses is { } ic)
        {
            var clean = IgnoredListsValidator.Clean(ic);
            if (!clean.SequenceEqual(settings.IgnoredClasses, System.StringComparer.OrdinalIgnoreCase))
            {
                settings.IgnoredClasses.Clear();
                settings.IgnoredClasses.AddRange(clean);
                plainChanged = true;
            }
        }
    }
}
```

- [ ] **Step 4: Ajouter le test apply + run vert**

Ajouter au même fichier de tests :

```csharp
[Fact]
public void Apply_IgnoredLists_DedupsPersistsAndNotifies()
{
    using var tmp = new TempSettingsScope();
    var bridge = new SettingsBridge(tmp.Controller, tmp.Service, appVersionProvider: () => "9.9.9");
    var resp = bridge.HandleMessage("""{"type":"apply","data":{"ignoredProcesses":["a.exe","","A.EXE "],"ignoredClasses":["Foo"]}}""");
    Assert.Single(tmp.Settings.IgnoredProcesses);
    Assert.Equal("a.exe", tmp.Settings.IgnoredProcesses[0]);
    Assert.Equal(1, tmp.Controller.OnSettingsChangedCount);
    using var doc = JsonDocument.Parse(resp!);
    Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
}
```

Run :

```powershell
dotnet test PeekDows.slnx --filter "SettingsBridgeIgnoredTests"
```

Expected : PASS (2/2).

- [ ] **Step 5: Livrer le snippet UI pour D (ne pas toucher `wwwroot/`)**

Créer `docs/snippets/ignored-ui.md` avec les blocs exacts à injecter (D fera copier-coller) :

```html
<section class="page" id="page-ignored">
  <h2>Ignored apps</h2>
  <p class="sub">One entry per line. Applied on Save.</p>
  <div class="card">
    <div class="t">Ignored processes</div>
    <textarea id="ignoredProcesses" rows="6" spellcheck="false"></textarea>
  </div>
  <div class="card">
    <div class="t">Ignored window classes</div>
    <textarea id="ignoredClasses" rows="6" spellcheck="false"></textarea>
  </div>
  <div class="card"><div class="t">Always ignored (built-in)</div><div class="d">SearchHost.exe, StartMenuExperienceHost.exe, Shell_TrayWnd, Progman… (read-only)</div></div>
</section>
```

```js
// ignored section — D: fusionner dans app.js EDITABLE_KEYS + setControlsFrom/syncDirty
// draft.ignoredProcesses <-> textarea split("\n"), draft.ignoredClasses idem
```

- [ ] **Step 6: Commit (fichiers A uniquement)**

```bash
git add src/PeekDows.App/Settings/SettingsDraft.Ignored.cs src/PeekDows.App/Settings/SettingsBridge.Ignored.cs src/PeekDows.App/Settings/IgnoredListsValidator.cs tests/PeekDows.Tests/SettingsBridgeIgnoredTests.cs docs/snippets/ignored-ui.md
git commit -m "feat(settings): bridge ignored-apps lists with dedup"
```

Garde open-source obligatoire (voir contrat).

---

## Task B: Hotkeys éditables 2-combos (Agent B — parallèle, fichiers exclusifs)

**Files (ne créer/modifier QUE ceux-ci) :**
- Create: `src/PeekDows.App/Settings/HotkeyParser.cs`
- Create: `src/PeekDows.App/Settings/SettingsDraft.Hotkeys.cs`
- Create: `src/PeekDows.App/Settings/SettingsBridge.Hotkeys.cs`
- Create: `src/PeekDows.App/Hotkeys/HotkeyService.TryUpdate.cs`
- Create: `src/PeekDows.App/PeekDowsAppContext.Hotkeys.cs`
- Create: `tests/PeekDows.Tests/HotkeyParserTests.cs`
- Create: `tests/PeekDows.Tests/SettingsBridgeHotkeysTests.cs`
- Create: `docs/snippets/hotkeys-ui.md`

Contexte : `AppSettings.cs:60-68` stocke 6 clés mais `HotkeyService.cs:36-82` ne registe que `arrangeNow=Ctrl+Alt+Space`, `pauseResume=Ctrl+Alt+P`. Scope = ces 2 uniquement. `NativeMethods.cs:28-32` (`MOD_ALT/CONTROL`, `VK_SPACE/VK_P`).

- [ ] **Step 1: Test parser qui échoue**

Créer `tests/PeekDows.Tests/HotkeyParserTests.cs` :

```csharp
using PeekDows.App.Settings;
using Xunit;

namespace PeekDows.Tests;

public sealed class HotkeyParserTests
{
    [Fact] public void Parse_CtrlAltSpace_Ok()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Space", out var mod, out var vk));
        Assert.Equal(0x0002u | 0x0001u, mod);
        Assert.Equal(0x20u, vk);
    }
    [Fact] public void Parse_CtrlAltP_Ok()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+P", out _, out var vk));
        Assert.Equal(0x50u, vk);
    }
    [Fact] public void Parse_ModifiersOnly_Rejected()
    {
        Assert.False(HotkeyParser.TryParse("Ctrl+Alt", out _, out _));
    }
    [Fact] public void Parse_Empty_Rejected()
    {
        Assert.False(HotkeyParser.TryParse("", out _, out _));
    }
    [Fact] public void RoundTrip_Format_Parse()
    {
        var s = HotkeyParser.Format(0x0002u | 0x0001u, 0x50u);
        Assert.Equal("Ctrl+Alt+P", s);
    }
}
```

- [ ] **Step 2: Run** `dotnet test PeekDows.slnx --filter "HotkeyParserTests"` Expected : FAIL (type inexistant).

- [ ] **Step 3: Créer `HotkeyParser.cs` (pur, sans P/Invoke)**

```csharp
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
```

Run : `dotnet test --filter HotkeyParserTests` → PASS (5/5).

- [ ] **Step 4: Partiel `HotkeyService.TryUpdate.cs` (généralise sans toucher le core)**

Créer `src/PeekDows.App/Hotkeys/HotkeyService.TryUpdate.cs` :

```csharp
using System;
using System.Runtime.InteropServices;
using PeekDows.App.Settings;
using PeekDows.Core.Win32;

namespace PeekDows.App.Hotkeys;

public sealed partial class HotkeyService
{
    private const int HotkeyArrangeId = 1;
    private const int HotkeyPauseId = 2;

    public static int IdForName(string name) => name switch
    {
        "arrangeNow" => HotkeyArrangeId,
        "pauseResume" => HotkeyPauseId,
        _ => -1,
    };

    /// <summary>Re-registre un hotkey. Retourne false + "conflict"/"bad-gesture" si échec, ancien conservé.</summary>
    public bool TryUpdateGesture(string name, string gesture, out string error)
    {
        error = "";
        int id = IdForName(name);
        if (id < 0) { error = "unknown-hotkey"; return false; }
        if (!HotkeyParser.TryParse(gesture, out var mod, out var vk)) { error = "bad-gesture"; return false; }
        try { if (_messageWindow.Handle != IntPtr.Zero) NativeMethods.UnregisterHotKey(_messageWindow.Handle, id); } catch { }
        bool ok = NativeMethods.RegisterHotKey(_messageWindow.Handle, id, mod, vk);
        if (!ok)
        {
            int win32 = Marshal.GetLastWin32Error();
            _logger?.Warn($"Hotkey update conflict: {name}={gesture} win32={win32}");
            error = "hotkey-conflict";
            return false;
        }
        if (id == HotkeyArrangeId) _arrangeRegistered = true;
        else _pauseRegistered = true;
        _logger?.Info($"Hotkey updated: {name}={gesture}");
        return true;
    }
}
```

Note : réutilise `_messageWindow`, `_logger`, `_arrangeRegistered`, `_pauseRegistered` du fichier core (même `partial`).

- [ ] **Step 5: Partiels draft + bridge + context**

Créer `src/PeekDows.App/Settings/SettingsDraft.Hotkeys.cs` :

```csharp
namespace PeekDows.App.Settings;

public sealed partial class SettingsDraft
{
    public System.Collections.Generic.Dictionary<string, string>? Hotkeys { get; set; }
}
```

Créer `src/PeekDows.App/Settings/SettingsBridge.Hotkeys.cs` :

```csharp
using System.Collections.Generic;

namespace PeekDows.App.Settings;

public sealed partial class SettingsBridge
{
    partial void AugmentSnapshotHotkeys(Dictionary<string, object?> data)
    {
        var h = _controller.CurrentSettings.Hotkeys;
        h ??= new Dictionary<string, string>();
        data["hotkeys"] = new Dictionary<string, string>
        {
            ["arrangeNow"] = h.TryGetValue("arrangeNow", out var a) ? a : "Ctrl+Alt+Space",
            ["pauseResume"] = h.TryGetValue("pauseResume", out var p) ? p : "Ctrl+Alt+P",
        };
    }

    partial void ApplyHotkeysDraft(SettingsDraft draft, ref string? hotkeyError)
    {
        if (draft.Hotkeys is null) return;
        foreach (var kv in draft.Hotkeys)
        {
            if (kv.Key is not ("arrangeNow" or "pauseResume")) continue;
            var cur = _controller.CurrentSettings.Hotkeys.TryGetValue(kv.Key, out var c) ? c : "";
            if (string.Equals(cur, kv.Value, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!_controller.TryUpdateHotkey(kv.Key, kv.Value, out _))
            {
                hotkeyError = "hotkey-conflict";
                return;
            }
        }
    }
}
```

Créer `src/PeekDows.App/PeekDowsAppContext.Hotkeys.cs` :

```csharp
using PeekDows.App.Hotkeys;

namespace PeekDows.App;

public partial class PeekDowsAppContext
{
    public bool TryUpdateHotkey(string name, string gesture, out string error)
    {
        error = "";
        if (!HotkeyParser.TryParse(gesture, out _, out _)) { error = "bad-gesture"; return false; }
        if (!_hotkeyService.TryUpdateGesture(name, gesture, out error)) return false;
        _settings.Hotkeys[name] = gesture;
        _settingsService.Save(_settings);
        _logger.Info($"Hotkey persisted: {name}={gesture}");
        return true;
    }
}
```

Créer `tests/PeekDows.Tests/SettingsBridgeHotkeysTests.cs` avec helper temp local : snapshot contient `hotkeys.arrangeNow`, apply même valeur ne touche rien, apply conflit (`FakeBridgeController.TryUpdateHotkey` retourne `false`) → `applied{ok:false, error:"hotkey-conflict"}`.

- [ ] **Step 6: Run**

```powershell
dotnet build PeekDows.slnx
dotnet test PeekDows.slnx --filter "HotkeyParserTests|SettingsBridgeHotkeysTests"
```

Expected : `0 Error(s)`, PASS.

- [ ] **Step 7: Snippet UI + commit**

Créer `docs/snippets/hotkeys-ui.md` (2 lignes capture + reset, protocole `draft.hotkeys = {arrangeNow, pauseResume}`).

```bash
git add src/PeekDows.App/Settings/HotkeyParser.cs src/PeekDows.App/Settings/SettingsDraft.Hotkeys.cs src/PeekDows.App/Settings/SettingsBridge.Hotkeys.cs src/PeekDows.App/Hotkeys/HotkeyService.TryUpdate.cs src/PeekDows.App/PeekDowsAppContext.Hotkeys.cs tests/PeekDows.Tests/HotkeyParserTests.cs tests/PeekDows.Tests/SettingsBridgeHotkeysTests.cs docs/snippets/hotkeys-ui.md
git commit -m "feat(settings): editable arrange/pause hotkeys with conflict revert"
```

Garde open-source obligatoire.

---

## Task C: Windows lecture-seule (Agent C — parallèle, fichiers exclusifs)

**Files (ne créer/modifier QUE ceux-ci) :**
- Create: `src/PeekDows.App/Settings/WindowsSnapshotProvider.cs`
- Create: `src/PeekDows.App/Settings/SettingsBridge.Windows.cs`
- Create: `src/PeekDows.App/PeekDowsAppContext.Windows.cs`
- Create: `tests/PeekDows.Tests/SettingsBridgeWindowsTests.cs`
- Create: `docs/snippets/windows-ui.md`

Utilise `WindowRow` (créé en Task 0). Ne touche JAMAIS `WindowDiscoveryService.cs`, `WindowClassifier.cs`, `SettingsBridge.cs` core.

- [ ] **Step 1: Test qui échoue**

Créer `tests/PeekDows.Tests/SettingsBridgeWindowsTests.cs` :

```csharp
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Settings;
using Xunit;

namespace PeekDows.Tests;

public sealed class SettingsBridgeWindowsTests
{
    [Fact]
    public void RequestWindows_ReturnsRows()
    {
        using var tmp = new TempSettingsScope();
        var rows = new List<WindowRow> { new("123", "Notepad", "notepad.exe", "Notepad", true, "") };
        var bridge = new SettingsBridge(tmp.Controller, tmp.Service,
            appVersionProvider: () => "9.9.9",
            windowsProvider: () => rows);
        using var doc = JsonDocument.Parse(bridge.HandleMessage("""{"type":"requestWindows"}""")!);
        Assert.Equal("windows", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetArrayLength());
    }
}
```

Note : le ctor `SettingsBridge(..., windowsProvider:)` n'existe pas encore → FAIL compilation (voulu). `TempSettingsScope` = helper local (copie minimale, données génériques `Notepad/notepad.exe` uniquement — aucune fenêtre réelle).

- [ ] **Step 2: Run** `dotnet test --filter RequestWindows_ReturnsRows` Expected : FAIL.

- [ ] **Step 3: Provider + partiel bridge + partiel context**

Créer `src/PeekDows.App/Settings/WindowsSnapshotProvider.cs` :

```csharp
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>Construit les lignes Windows depuis discovery+classifier. Lecture-seule, cap 200.</summary>
public static class WindowsSnapshotProvider
{
    public static IReadOnlyList<WindowRow> Build(
        WindowDiscoveryService discovery,
        WindowClassifier classifier,
        int cap = 200)
    {
        var raws = discovery.GetTopLevelWindows();
        var list = new List<WindowRow>(raws.Count);
        foreach (var r in raws.Take(cap))
        {
            bool eligible = classifier.IsEligible(r);
            string reason = eligible ? "" : ReasonFor(r, classifier);
            list.Add(new WindowRow(
                r.Hwnd.ToString(),
                r.Title ?? "",
                r.ProcessName ?? "",
                r.ClassName ?? "",
                eligible, reason));
        }
        return list.OrderBy(x => x.Process).ThenBy(x => x.Title).ToList();
    }

    private static string ReasonFor(Core.Models.RawWindowInfo r, WindowClassifier c)
    {
        if (c.IsIgnoredProcess(r.ProcessName)) return "ignored-process";
        if (c.IsIgnoredClass(r.ClassName)) return "ignored-class";
        if (c.IsSystemWindow(r)) return "system";
        return "ineligible";
    }
}
```

Créer `src/PeekDows.App/Settings/SettingsBridge.Windows.cs` (handler statique, wiré par D) :

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PeekDows.App.Settings;

/// <summary>Handler requestWindows. Enregistré via SetWindowsMessageHandler + SetWindowsProvider (D).</summary>
public static class WindowsMessageHandler
{
    public static string? TryHandle(
        string type,
        JsonElement root,
        Func<IReadOnlyList<WindowRow>>? provider,
        JsonSerializerOptions writeOptions)
    {
        if (type != "requestWindows") return null;
        var rows = provider?.Invoke() ?? (IReadOnlyList<WindowRow>)Array.Empty<WindowRow>();
        return JsonSerializer.Serialize(new { type = "windows", data = rows }, writeOptions);
    }

    public static JsonSerializerOptions CamelCase => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
```

Note : NE PAS redéclarer `_windowsProvider` ni `TryHandleWindowsMessage` dans ce fichier (le core Task 0 possède déjà le champ + `SetWindowsProvider` + `SetWindowsMessageHandler`). L'Agent C fournit le handler + le provider ; D les branche dans `SettingsHostForm` :

```csharp
_bridge.SetWindowsProvider(() => _controller.GetWindowsSnapshot());
_bridge.SetWindowsMessageHandler((type, root) =>
    WindowsMessageHandler.TryHandle(type, root, () => _controller.GetWindowsSnapshot(), WindowsMessageHandler.CamelCase));
```

Pour les tests, appeler `WindowsMessageHandler.TryHandle` directement (pas besoin du bridge) + 1 test bridge via `SetWindowsMessageHandler`.

Créer `src/PeekDows.App/PeekDowsAppContext.Windows.cs` :

```csharp
using System.Collections.Generic;

namespace PeekDows.App;

public partial class PeekDowsAppContext
{
    public System.Collections.Generic.IReadOnlyList<Settings.WindowRow> GetWindowsSnapshot()
        => Settings.WindowsSnapshotProvider.Build(_discoveryService, _classifier);
}
```

- [ ] **Step 4: Run vert**

```powershell
dotnet build PeekDows.slnx
dotnet test PeekDows.slnx --filter "SettingsBridgeWindowsTests"
```

Expected : `0 Error(s)`, PASS.

- [ ] **Step 5: Snippet UI + commit**

Créer `docs/snippets/windows-ui.md` (table filtrante + Refresh `post({type:"requestWindows"})` + bouton “Ignorer ce process” → copie vers draft ignored).

```bash
git add src/PeekDows.App/Settings/WindowsSnapshotProvider.cs src/PeekDows.App/Settings/SettingsBridge.Windows.cs src/PeekDows.App/PeekDowsAppContext.Windows.cs tests/PeekDows.Tests/SettingsBridgeWindowsTests.cs docs/snippets/windows-ui.md
git commit -m "feat(settings): read-only windows snapshot provider"
```

Garde open-source obligatoire.

---

## Task D: Shell UI + docs + gates (Agent D — APRÈS A+B+C)

**Files (exclusif D) :**
- Modify: `src/PeekDows.App/wwwroot/index.html`, `app.js`, `app.css`
- Modify: `src/PeekDows.App/Settings/SettingsHostForm.cs` (1 injection provider)
- Modify: `docs/MANUAL_TEST_CHECKLIST.md`, `README.md`

Préconditions : A+B+C mergés, `dotnet build` vert.

- [ ] **Step 1: Activer la nav (retirer `disabled`)**

Dans `index.html:26-37`, remplacer les 3 boutons `disabled` par :

```html
<button class="navitem" data-page="windows">Windows</button>
<button class="navitem" data-page="hotkeys">Hotkeys</button>
<button class="navitem" data-page="ignored">Ignored apps</button>
```

Ajouter les 3 `<section id="page-windows|hotkeys|ignored">` en collant les blocs de `docs/snippets/ignored-ui.md`, `hotkeys-ui.md`, `windows-ui.md`. Tabs JS déjà génériques (`app.js:133-140`), aucune logique tab à écrire.

- [ ] **Step 2: Étendre `EDITABLE_KEYS` + sync**

Dans `app.js:3-11` :

```js
const EDITABLE_KEYS = [
  "enabled", "autoArrange", "animate", "directionalFocus", "startWithWindows",
  "allowRepositionMaximized", "preset", "arrangeOnStartup",
  "showTrayNotifications", "thresholdPx",
  "ignoredProcesses", "ignoredClasses", "hotkeys",
];
```

`setControlsFrom` : remplir textareas (`join("\n")`), boutons hotkeys (texte = valeur), table Windows vide jusqu'au Refresh. `syncDirty()` (JSON.stringify) déjà compatible arrays/objets — ne pas réécrire.

- [ ] **Step 3: Brancher le provider Windows**

Dans `SettingsHostForm.cs`, après `_bridge = new SettingsBridge(...)`, ajouter :

```csharp
_bridge.SetWindowsProvider(() => _controller.GetWindowsSnapshot());
_bridge.SetWindowsMessageHandler((type, root) =>
    WindowsMessageHandler.TryHandle(type, root, () => _controller.GetWindowsSnapshot(), WindowsMessageHandler.CamelCase));
```

- [ ] **Step 4: CSS minimale** : réutiliser `.card/.row/.btn/.kbd`, ajouter `.table,.textarea,.capture` dans le même langage (pas de framework).

- [ ] **Step 5: Docs publiques**

`README.md:84` : remplacer `Hotkeys is persisted but not yet editable via the settings UI` par `Hotkeys arrangeNow/pauseResume are editable from Settings → Hotkeys`.
`MANUAL_TEST_CHECKLIST.md § Settings App v2` : ajouter 3 lignes (ignored round-trip, hotkey conflit `Ctrl+Alt+P→O` + revert, windows Refresh + “Ignorer ce process”). Aucun chemin local, aucun e-mail, aucune info machine.

- [ ] **Step 6: Gates finaux**

```powershell
dotnet build PeekDows.slnx
dotnet test PeekDows.slnx
git status --short
```

Expected : `0 Error(s)`, `Passed!`, `git status` uniquement les fichiers D + snippets déjà mergés. Checklist manuelle : 100/125/150% DPI + fallback `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` invalide → legacy + log warn.

- [ ] **Step 7: Commit**

```bash
git add src/PeekDows.App/wwwroot/index.html src/PeekDows.App/wwwroot/app.js src/PeekDows.App/wwwroot/app.css src/PeekDows.App/Settings/SettingsHostForm.cs docs/MANUAL_TEST_CHECKLIST.md README.md
git commit -m "feat(settings): enable windows/hotkeys/ignored pages v2"
```

Garde open-source obligatoire.

---

## Self-review

1. Spec coverage : Windows lecture-seule ✓ (C+D), Hotkeys 2-combos avec conflit ✓ (B+D), Ignored avec dedup ✓ (A+D), fallback legacy gardé ✓ (D ne touche pas `TrayIconController`), dirty/Save/banner réutilisés ✓.
2. Placeholder scan : aucun `TBD/TODO` — chaque step a code + commande + Expected.
3. Type consistency : `WindowRow(Hwnd,Title,Process,ClassName,Eligible,Reason)`, `TryUpdateHotkey(name,gesture,out error)→bool`, clés JSON camelCase `ignoredProcesses/ignoredClasses/hotkeys/windows`, messages `settings/externalChange/applied/error + windows`.
4. Parallel safety : Task 0 d'abord, puis A/B/C fichiers disjoints (partiels + tests + snippets séparés), D seul touche `wwwroot/` + `HostForm` + docs. `git add` restreint par owner.
