using System;
using System.Collections.Generic;
using System.Text.Json;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Agent F3 — contrat bridge des appRules (S2, câblé par l'intégrateur dans
/// SettingsBridge.Beta.cs + SettingsDraft.Beta.cs) :
/// snapshot expose appRules (défaut []), apply valide (process vide rejeté,
/// action inconnue rejetée, slot invalide pour pin rejeté, ignore sans slot
/// accepté), dirty tracking (mêmes valeurs → pas de notify).
///
/// NOTE D'INTÉGRATION : la prop AppSettings.AppRules (S1) et le câblage bridge
/// (S2) n'existent pas encore — ces tests sont donc AUTO-PORTÉS : ils exercent
/// le contrat exact via un FAKE controller LOCAL (aucun fake partagé touché,
/// aucun SettingsBridge réel utilisé) et les helpers miroirs du snippet S2
/// ci-dessous (désérialisation JsonElement→List&lt;AppRule&gt; + CleanRules +
/// AreRulesEqual). Quand S2 atterrit, la logique est identique — seul le
/// backing store passe du fake à settings.AppRules.
/// </summary>
public sealed class AppRuleBridgeTests
{
    private static readonly JsonSerializerOptions BridgeReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions BridgeWriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ----- miroir local du snippet S2 (apply) -----
    private static bool ApplyAppRules(FakeAppRuleController controller, JsonElement data)
    {
        if (!data.TryGetProperty("appRules", out var el))
            return false; // clé absente = "untouched", comme les autres clés du draft
        List<AppRule>? incoming;
        try
        {
            incoming = el.Deserialize<List<AppRule>>(BridgeReadOptions);
        }
        catch
        {
            return false; // payload malformé : rejeté, sans notify
        }
        if (incoming is null)
            return false;
        var clean = AppRuleService.CleanRules(incoming);
        if (AppRuleService.AreRulesEqual(controller.AppRules, clean))
            return false; // dirty tracking : mêmes valeurs → pas de notify
        controller.AppRules = clean;
        controller.OnSettingsChanged();
        return true;
    }

    // ----- miroir local du snippet S2 (snapshot) -----
    private static string BuildAppRulesSnapshot(FakeAppRuleController controller)
        => JsonSerializer.Serialize(
            new { type = "settings", data = new { appRules = controller.AppRules } },
            BridgeWriteOptions);

    private static JsonElement ApplyData(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static JsonElement SnapshotData(string snapshot)
    {
        using var doc = JsonDocument.Parse(snapshot);
        return doc.RootElement.GetProperty("data").Clone();
    }

    // ----- snapshot -----

    [Fact]
    public void Snapshot_ExposesAppRules_DefaultEmpty()
    {
        var controller = new FakeAppRuleController();

        var data = SnapshotData(BuildAppRulesSnapshot(controller));

        Assert.Equal(JsonValueKind.Array, data.GetProperty("appRules").ValueKind);
        Assert.Equal(0, data.GetProperty("appRules").GetArrayLength());
    }

    [Fact]
    public void Snapshot_ExposesAppRules_WithEntries()
    {
        var controller = new FakeAppRuleController();
        controller.AppRules = new List<AppRule>
        {
            new() { Process = "notepad.exe", Action = "pin", Slot = "A" },
            new() { Process = "gamebar", Action = "ignore", Slot = "" },
        };

        var data = SnapshotData(BuildAppRulesSnapshot(controller));
        var arr = data.GetProperty("appRules");

        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("notepad.exe", arr[0].GetProperty("process").GetString());
        Assert.Equal("pin", arr[0].GetProperty("action").GetString());
        Assert.Equal("A", arr[0].GetProperty("slot").GetString());
        Assert.Equal("ignore", arr[1].GetProperty("action").GetString());
    }

    [Fact]
    public void Snapshot_UsesCamelCaseKeys()
    {
        var controller = new FakeAppRuleController();
        controller.AppRules = new List<AppRule> { new() { Process = "a.exe", Action = "pin", Slot = "A" } };

        var snapshot = BuildAppRulesSnapshot(controller);

        Assert.Contains("\"appRules\"", snapshot);
        Assert.Contains("\"process\"", snapshot);
        Assert.Contains("\"action\"", snapshot);
        Assert.Contains("\"slot\"", snapshot);
        Assert.DoesNotContain("AppRules", snapshot);
        Assert.DoesNotContain("\"Process\"", snapshot);
    }

    // ----- apply : validation -----

    [Fact]
    public void Apply_ValidPinAndIgnore_PersistAndNotify()
    {
        var controller = new FakeAppRuleController();
        var data = ApplyData("""{"data":{"appRules":[{"process":"notepad.exe","action":"pin","slot":"A"},{"process":"gamebar","action":"ignore"}]}}""");

        var changed = ApplyAppRules(controller, data);

        Assert.True(changed);
        Assert.Equal(2, controller.AppRules.Count);
        Assert.Equal(1, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_EmptyProcess_Rejected()
    {
        var controller = new FakeAppRuleController();
        var data = ApplyData("""{"data":{"appRules":[{"process":"","action":"pin","slot":"A"},{"process":"notepad.exe","action":"pin","slot":"A"}]}}""");

        ApplyAppRules(controller, data);

        Assert.Single(controller.AppRules);
        Assert.Equal("notepad.exe", controller.AppRules[0].Process);
    }

    [Fact]
    public void Apply_UnknownAction_Rejected()
    {
        var controller = new FakeAppRuleController();
        var data = ApplyData("""{"data":{"appRules":[{"process":"notepad.exe","action":"float","slot":"A"},{"process":"calc.exe","action":"pin","slot":"B"}]}}""");

        ApplyAppRules(controller, data);

        Assert.Single(controller.AppRules);
        Assert.Equal("calc.exe", controller.AppRules[0].Process);
    }

    [Fact]
    public void Apply_InvalidSlotForPin_Rejected()
    {
        var controller = new FakeAppRuleController();
        var data = ApplyData("""{"data":{"appRules":[{"process":"a.exe","action":"pin","slot":"Z"},{"process":"b.exe","action":"pin","slot":""},{"process":"c.exe","action":"pin","slot":"C"}]}}""");

        ApplyAppRules(controller, data);

        Assert.Single(controller.AppRules);
        Assert.Equal("c.exe", controller.AppRules[0].Process);
    }

    [Fact]
    public void Apply_IgnoreWithoutSlot_Accepted()
    {
        var controller = new FakeAppRuleController();
        // Slot omis (cas UI "ignore sans slot") + slot vide explicite.
        var data = ApplyData("""{"data":{"appRules":[{"process":"gamebar","action":"ignore"},{"process":"overlay","action":"ignore","slot":""}]}}""");

        var changed = ApplyAppRules(controller, data);

        Assert.True(changed);
        Assert.Equal(2, controller.AppRules.Count);
        Assert.All(controller.AppRules, r => Assert.Equal("ignore", r.Action));
        Assert.Equal(1, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_MalformedAppRules_NoChangeNoNotify()
    {
        var controller = new FakeAppRuleController();
        var data = ApplyData("""{"data":{"appRules":"not-an-array"}}""");

        var changed = ApplyAppRules(controller, data);

        Assert.False(changed);
        Assert.Empty(controller.AppRules);
        Assert.Equal(0, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_ActionIsCaseInsensitive()
    {
        var controller = new FakeAppRuleController();
        var data = ApplyData("""{"data":{"appRules":[{"process":"a.exe","action":"PIN","slot":"b"}]}}""");

        ApplyAppRules(controller, data);

        Assert.Single(controller.AppRules);
        Assert.Equal("pin", controller.AppRules[0].Action);
        Assert.Equal("B", controller.AppRules[0].Slot);
    }

    // ----- apply : dirty tracking -----

    [Fact]
    public void Apply_MissingAppRules_Untouched_NoNotify()
    {
        var controller = new FakeAppRuleController();
        var data = ApplyData("""{"data":{"preset":"Small"}}""");

        var changed = ApplyAppRules(controller, data);

        Assert.False(changed);
        Assert.Equal(0, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_SameRules_DoesNotNotify()
    {
        var controller = new FakeAppRuleController();
        controller.AppRules = new List<AppRule> { new() { Process = "notepad.exe", Action = "pin", Slot = "A" } };
        var data = ApplyData("""{"data":{"appRules":[{"process":"NOTEPAD.exe","action":"pin","slot":"a"}]}}""");

        var changed = ApplyAppRules(controller, data);

        Assert.False(changed);
        Assert.Equal(0, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Apply_DifferentRules_Notifies()
    {
        var controller = new FakeAppRuleController();
        controller.AppRules = new List<AppRule> { new() { Process = "notepad.exe", Action = "pin", Slot = "A" } };
        var data = ApplyData("""{"data":{"appRules":[{"process":"notepad.exe","action":"pin","slot":"B"}]}}""");

        var changed = ApplyAppRules(controller, data);

        Assert.True(changed);
        Assert.Equal("B", controller.AppRules[0].Slot);
        Assert.Equal(1, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void Snapshot_Apply_RoundTrip_IsStable()
    {
        var controller = new FakeAppRuleController();
        controller.AppRules = new List<AppRule>
        {
            new() { Process = "notepad.exe", Action = "pin", Slot = "A" },
            new() { Process = "gamebar", Action = "ignore", Slot = "" },
        };

        // Le snapshot sérialisé re-appliqué à un store vide redonne les mêmes règles
        // sans second notify (symétrie snapshot ⇄ apply, base du dirty tracking JS).
        var snapshot = BuildAppRulesSnapshot(controller);
        var fresh = new FakeAppRuleController();
        var data = SnapshotData(snapshot);
        var changedOnce = ApplyAppRules(fresh, data);
        var data2 = SnapshotData(BuildAppRulesSnapshot(fresh));
        var changedTwice = ApplyAppRules(fresh, data2);

        Assert.True(changedOnce);
        Assert.False(changedTwice);
        Assert.True(AppRuleService.AreRulesEqual(controller.AppRules, fresh.AppRules));
    }

    /// <summary>
    /// Fake controller LOCAL (aucun fake partagé touché) — même surface que
    /// SettingsBridgeBetaTests. AppRules vit ici en attendant la prop
    /// AppSettings.AppRules (S1) ; ApplyAppRules/BuildAppRulesSnapshot migrent
    /// tels quels en S2 (settings.AppRules comme backing store).
    /// </summary>
    private sealed class FakeAppRuleController : IPeekDowsController
    {
        public FakeAppRuleController()
        {
            CurrentSettings = new AppSettings();
        }

        public List<AppRule> AppRules { get; set; } = new();
        public int OnSettingsChangedCount { get; private set; }

        public RuntimeState State => RuntimeState.Running;
        public string LogFilePath => "";
        public AppSettings CurrentSettings { get; }
        public bool IsAutoArrangeRunning => false;
        public bool IsStartWithWindowsEnabled => CurrentSettings.StartWithWindows;
        public bool IsDirectionalFocusEnabled => CurrentSettings.DirectionalFocusEnabled;
        public bool IsAnimateWindowTransitionsEnabled => CurrentSettings.AnimateWindowTransitions;
        public bool AllowRepositionMaximizedWindows => CurrentSettings.AllowRepositionMaximizedWindows;
        public WindowSizePreset CurrentWindowSizePreset => CurrentSettings.WindowSizePreset;
        public bool IsPaused => false;
        public DateTimeOffset? PauseUntil => null;
        public string? PauseDescription => null;

        public event Action<RuntimeState>? StateChanged;
        public event Action<bool>? AutoArrangeChanged;
        public event Action<bool>? StartWithWindowsChanged;
        public event Action<bool>? DirectionalFocusChanged;
        public event Action<bool>? AnimateWindowTransitionsChanged;
        public event Action<bool>? AllowRepositionMaximizedWindowsChanged;
        public event Action<WindowSizePreset>? WindowSizePresetChanged;
        public event Action<string>? FocusHintModeChanged;
        public event Action<bool>? SoundChanged;

        public void ArrangeNow() { }
        public void TogglePause() { }
        public void PauseFor(TimeSpan duration) { }
        public void PauseUntilResumed() { }
        public void Resume() { }
        public void OpenSettings() { }
        public void OpenLogFile() { }
        public void OpenLogsFolder() { }
        public void Exit() { }
        public void OnSettingsChanged() { OnSettingsChangedCount++; }

        public void ToggleAutoArrange() { }
        public void ToggleStartWithWindows() { }
        public void ToggleDirectionalFocus() { }
        public void ToggleAnimateWindowTransitions() { }
        public void ToggleAllowRepositionMaximizedWindows() { }
        public void SetWindowSizePreset(WindowSizePreset preset) { }
        public void SetFocusHintMode(string mode) { }
        public void ToggleArrangeSound() { }
        public void RefreshArrangeSoundAutoOff() { }
        public void DisableArrangeSound(string reason) { }
        public string[] GetUserSounds() => Array.Empty<string>();
        public string PickArrangeSoundFile() => "";
        public void DeleteArrangeSoundFile(string name) { }
        public void PreviewArrangeSound(string name) { }
        public void UndoArrange() { }
        public bool CanUndoArrange => false;
        public void SetLayoutMode(string mode) { CurrentSettings.LayoutMode = mode; }
        public event Action<string>? LayoutModeChanged;
    }
}
