using PeekDows.App.Settings;
using PeekDows.Core.Models;

namespace PeekDows.App;

public partial class PeekDowsAppContext
{
    public bool TryCommitSettings(AppSettings next, out string error)
    {
        var previous = _settings;
        return SettingsCommitter.TryCommit(previous, next, _startupService, _hotkeyService.TryUpdateGestures,
            _settingsService.Save, settings => PublishSettings(previous, settings), () => IsPaused, ArrangeNow,
            _logger.Error, out error);
    }

    private void PublishSettings(AppSettings previous, AppSettings next)
    {
        _settings = next;
        OnSettingsChanged();

        bool autoArrangeChanged = previous.AutoArrange != next.AutoArrange;
        bool directionalFocusChanged = previous.DirectionalFocusEnabled != next.DirectionalFocusEnabled;
        bool animationChanged = previous.AnimateWindowTransitions != next.AnimateWindowTransitions;
        bool soundChanged = previous.ArrangeSoundEnabled != next.ArrangeSoundEnabled;

        bool autoArrangeApplied = !autoArrangeChanged || ApplyEffect("Auto Arrange", () =>
        {
            if (next.AutoArrange) _autoArrangeService.Start(next);
            else _autoArrangeService.Stop();
        });

        bool focusApplied = !directionalFocusChanged || ApplyEffect("directional focus", () =>
        {
            if (next.DirectionalFocusEnabled) _directionalFocusService.Start();
            else _directionalFocusService.Stop();
        });

        bool animationApplied = !animationChanged || next.AnimateWindowTransitions
            || ApplyEffect("window animation", _animationService.SnapAllToTarget);

        if (soundChanged || previous.ArrangeSoundAutoOffMinutes != next.ArrangeSoundAutoOffMinutes)
            RefreshArrangeSoundAutoOff();

        if (autoArrangeChanged && autoArrangeApplied)
            ApplyEffect("Auto Arrange notification", () => AutoArrangeChanged?.Invoke(next.AutoArrange));
        if (directionalFocusChanged && focusApplied)
            ApplyEffect("directional focus notification", () => DirectionalFocusChanged?.Invoke(next.DirectionalFocusEnabled));
        if (animationChanged && animationApplied)
            ApplyEffect("window animation notification", () => AnimateWindowTransitionsChanged?.Invoke(next.AnimateWindowTransitions));
        if (previous.StartWithWindows != next.StartWithWindows)
            ApplyEffect("startup notification", () => StartWithWindowsChanged?.Invoke(next.StartWithWindows));
        if (previous.AllowRepositionMaximizedWindows != next.AllowRepositionMaximizedWindows)
            ApplyEffect("maximized windows notification", () => AllowRepositionMaximizedWindowsChanged?.Invoke(next.AllowRepositionMaximizedWindows));
        if (previous.WindowSizePreset != next.WindowSizePreset)
            ApplyEffect("window size notification", () => WindowSizePresetChanged?.Invoke(next.WindowSizePreset));
        if (previous.LayoutMode != next.LayoutMode)
            ApplyEffect("layout mode notification", () => LayoutModeChanged?.Invoke(next.LayoutMode));
        if (previous.FocusHintMode != next.FocusHintMode)
            ApplyEffect("focus hint notification", () => FocusHintModeChanged?.Invoke(next.FocusHintMode));
        if (soundChanged)
            ApplyEffect("arrange sound notification", () => SoundChanged?.Invoke(next.ArrangeSoundEnabled));
    }

    private bool ApplyEffect(string operation, Action effect)
    {
        try
        {
            effect();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not apply {operation} after saving settings", ex);
            return false;
        }
    }
}
