using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PeekDows.Tests;

/// <summary>
/// Agent F2 — contrat bridge du layoutMode (fichier F2-exclusif, fake LOCAL).
/// Contexte: AppSettings.LayoutMode + SettingsBridge.Beta (S2) + IPeekDowsController
/// (S6) sont posés par l'intégrateur; ce fichier ne dépend d'AUCUN d'eux et reste
/// vert avant comme après intégration. Il fige la norme que S2/S6 doivent respecter :
/// canonique Grid|Halves|Thirds, snapshot expose "layoutMode" (défaut "Grid"),
/// apply normalise case-insensitive (+ alias 1 lettre g/h/t), invalide/vide ignoré
/// côté bridge (pas de dirty), coercition → Grid côté controller.
/// Pattern calqué sur SettingsBridgeBetaTests (fake minimal local au fichier).
/// DÉCISIONS F2 (spec ambigüe "t"/"vide → inchangé?") :
/// - "t" accepté comme alias de "Thirds" (comme "g"→Grid, "h"→Halves), documenté.
/// - vide/null côté bridge = inchangé (ignoré, jamais dirty), pas → Grid.
/// </summary>
public sealed class LayoutModeBridgeTests
{
    // ----- Miroir normatif du normalizer S2/S6 (intégrateur : garder en sync) -----

    private static class LayoutModeContract
    {
        public const string Default = "Grid";

        public static bool TryNormalizeLayoutMode(string? raw, out string canonical)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var v = raw.Trim();
                if (string.Equals(v, "Grid", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "g", StringComparison.OrdinalIgnoreCase)) { canonical = "Grid"; return true; }
                if (string.Equals(v, "Halves", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "h", StringComparison.OrdinalIgnoreCase)) { canonical = "Halves"; return true; }
                if (string.Equals(v, "Thirds", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "t", StringComparison.OrdinalIgnoreCase)) { canonical = "Thirds"; return true; }
            }
            canonical = string.Empty;
            return false;
        }

        /// <summary>Coercition côté controller (S6, pattern NormalizeFocusHintMode) : invalide → Grid.</summary>
        public static string NormalizeLayoutModeOrDefault(string? raw) =>
            TryNormalizeLayoutMode(raw, out var canonical) ? canonical : Default;

        /// <summary>
        /// Règle apply côté bridge (S2, pattern TryNormalizeHintMode) : invalide/vide
        /// ignoré (pas de dirty), même valeur ignorée (pas de notify).
        /// </summary>
        public static bool ApplyLayoutModeDraft(ref string current, string? raw)
        {
            if (TryNormalizeLayoutMode(raw, out var canonical)
                && !string.Equals(current, canonical, StringComparison.Ordinal))
            {
                current = canonical;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Fake controller MINIMAL local au fichier (ne touche pas aux fakes partagés).
    /// Mime la sémantique S6 : normalize + Save + notify, silencieux si inchangé.
    /// </summary>
    private sealed class FakeLayoutModeController
    {
        public string LayoutMode { get; private set; } = LayoutModeContract.Default;
        public string PersistedLayoutMode { get; private set; } = LayoutModeContract.Default;
        public int OnSettingsChangedCount { get; private set; }
        public event Action<string>? LayoutModeChanged;

        public void SetLayoutMode(string? mode)
        {
            var normalized = LayoutModeContract.NormalizeLayoutModeOrDefault(mode);
            if (string.Equals(LayoutMode, normalized, StringComparison.Ordinal))
                return;
            LayoutMode = normalized;
            PersistedLayoutMode = normalized; // mime SettingsService.Save
            OnSettingsChangedCount++;
            LayoutModeChanged?.Invoke(normalized);
        }

        public Dictionary<string, object?> SnapshotData() =>
            new(StringComparer.Ordinal) { ["layoutMode"] = LayoutMode ?? LayoutModeContract.Default };
    }

    /// <summary>Format wire du draft (clé camelCase, lecture case-insensitive comme le vrai bridge).</summary>
    private sealed class LayoutDraft
    {
        [JsonPropertyName("layoutMode")]
        public string? LayoutMode { get; set; }
    }

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    // ----- Normalisation -----

    [Theory]
    [InlineData("grid", "Grid")]
    [InlineData("Grid", "Grid")]
    [InlineData("GRID", "Grid")]
    [InlineData("g", "Grid")]
    [InlineData("G", "Grid")]
    [InlineData("halves", "Halves")]
    [InlineData("Halves", "Halves")]
    [InlineData("HALVES", "Halves")]
    [InlineData("h", "Halves")]
    [InlineData("thirds", "Thirds")]
    [InlineData("Thirds", "Thirds")]
    [InlineData("THIRDS", "Thirds")]
    [InlineData("t", "Thirds")]
    [InlineData("T", "Thirds")]
    [InlineData(" grid ", "Grid")]
    public void TryNormalize_ValidInputs_ReturnCanonical(string raw, string expected)
    {
        Assert.True(LayoutModeContract.TryNormalizeLayoutMode(raw, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("Yolo")]
    [InlineData("Half")]
    [InlineData("2")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_InvalidOrEmpty_ReturnsFalse(string? raw)
    {
        Assert.False(LayoutModeContract.TryNormalizeLayoutMode(raw, out _));
    }

    [Fact]
    public void TryNormalize_Null_ReturnsFalse()
    {
        Assert.False(LayoutModeContract.TryNormalizeLayoutMode(null, out _));
    }

    [Theory]
    [InlineData("Yolo", "Grid")]
    [InlineData("", "Grid")]
    [InlineData(null, "Grid")]
    [InlineData("   ", "Grid")]
    public void NormalizeOrDefault_InvalidOrEmpty_CoercesToGrid(string? raw, string expected)
    {
        Assert.Equal(expected, LayoutModeContract.NormalizeLayoutModeOrDefault(raw));
    }

    // ----- Fake controller : snapshot / apply / dirty tracking -----

    [Fact]
    public void Snapshot_DefaultLayoutMode_IsGrid()
    {
        var controller = new FakeLayoutModeController();

        Assert.Equal("Grid", controller.SnapshotData()["layoutMode"]);
    }

    [Fact]
    public void Snapshot_ReflectsMutatedLayoutMode()
    {
        var controller = new FakeLayoutModeController();
        controller.SetLayoutMode("Thirds");

        Assert.Equal("Thirds", controller.SnapshotData()["layoutMode"]);
    }

    [Fact]
    public void SetLayoutMode_HalvesUppercase_NormalizesPersistsAndNotifies()
    {
        var controller = new FakeLayoutModeController();
        string? raised = null;
        controller.LayoutModeChanged += m => raised = m;

        controller.SetLayoutMode("HALVES");

        Assert.Equal("Halves", controller.LayoutMode);
        Assert.Equal("Halves", controller.PersistedLayoutMode);
        Assert.Equal(1, controller.OnSettingsChangedCount);
        Assert.Equal("Halves", raised);
    }

    [Fact]
    public void SetLayoutMode_ShortAlias_T_NormalizesToThirds()
    {
        var controller = new FakeLayoutModeController();

        controller.SetLayoutMode("t");

        Assert.Equal("Thirds", controller.LayoutMode);
        Assert.Equal(1, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void SetLayoutMode_SameValue_DoesNotNotify()
    {
        var controller = new FakeLayoutModeController();
        controller.SetLayoutMode("Grid");

        Assert.Equal(0, controller.OnSettingsChangedCount);
    }

    [Fact]
    public void SetLayoutMode_Invalid_CoercesToGrid()
    {
        var controller = new FakeLayoutModeController();
        controller.SetLayoutMode("Halves");
        Assert.Equal(1, controller.OnSettingsChangedCount);

        controller.SetLayoutMode("Yolo");

        Assert.Equal("Grid", controller.LayoutMode);
        Assert.Equal(2, controller.OnSettingsChangedCount);
    }

    // ----- Règle apply bridge : invalide/vide ignoré, dirty tracking -----

    [Theory]
    [InlineData("grid", "Grid")]
    [InlineData("HALVES", "Halves")]
    [InlineData("t", "Thirds")]
    public void ApplyDraft_ValidInput_AppliesAndReportsDirty(string raw, string expected)
    {
        var current = expected == "Grid" ? "Halves" : "Grid";

        Assert.True(LayoutModeContract.ApplyLayoutModeDraft(ref current, raw));
        Assert.Equal(expected, current);
    }

    [Theory]
    [InlineData("Yolo")]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyDraft_InvalidOrEmpty_IgnoredWithoutDirty(string? raw)
    {
        var current = "Halves";

        Assert.False(LayoutModeContract.ApplyLayoutModeDraft(ref current, raw));
        Assert.Equal("Halves", current);
    }

    [Fact]
    public void ApplyDraft_NullRaw_IgnoredWithoutDirty()
    {
        var current = "Halves";

        Assert.False(LayoutModeContract.ApplyLayoutModeDraft(ref current, null));
        Assert.Equal("Halves", current);
    }

    [Fact]
    public void ApplyDraft_SameValue_NoDirty()
    {
        var current = "Thirds";

        Assert.False(LayoutModeContract.ApplyLayoutModeDraft(ref current, "THIRDS"));
        Assert.Equal("Thirds", current);
    }

    // ----- Format wire JSON -----

    [Fact]
    public void DraftJson_LayoutModeKey_DeserializesCaseInsensitively()
    {
        var draft = JsonSerializer.Deserialize<LayoutDraft>(
            """{"layoutMode":"HALVES"}""", ReadOptions);

        Assert.NotNull(draft);
        Assert.Equal("HALVES", draft!.LayoutMode);
        Assert.True(LayoutModeContract.TryNormalizeLayoutMode(draft.LayoutMode, out var canonical));
        Assert.Equal("Halves", canonical);
    }

    [Fact]
    public void DraftJson_MissingKey_MeansUntouched()
    {
        var draft = JsonSerializer.Deserialize<LayoutDraft>("{}", ReadOptions);

        Assert.NotNull(draft);
        Assert.Null(draft!.LayoutMode);
    }

    [Fact]
    public void SnapshotJson_LayoutModeKey_RoundTrips()
    {
        var controller = new FakeLayoutModeController();
        controller.SetLayoutMode("Thirds");

        var json = JsonSerializer.Serialize(
            new { type = "settings", data = controller.SnapshotData() },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("Thirds", doc.RootElement.GetProperty("data").GetProperty("layoutMode").GetString());
    }
}
