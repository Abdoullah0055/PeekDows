namespace PeekDows.App;

public partial class PeekDowsAppContext
{
    internal static IReadOnlyDictionary<string, string> DefaultHotkeys { get; } =
        new Dictionary<string, string>
        {
            ["arrangeNow"] = "Ctrl+Alt+Space",
            ["pauseResume"] = "Ctrl+Alt+P",
        };

    private void RegisterConfiguredHotkeys()
    {
        // Give saved shortcuts priority over fallback defaults.
        foreach (var name in DefaultHotkeys.Keys)
        {
            if (_settings.Hotkeys.TryGetValue(name, out var gesture)
                && !_hotkeyService.TryUpdateGestures(new Dictionary<string, string> { [name] = gesture }, out var error))
                _logger.Warn($"Could not register saved shortcut {name}: {error}");
        }
        foreach (var (name, gesture) in DefaultHotkeys)
        {
            if (!_hotkeyService.IsRegistered(name)
                && !_hotkeyService.TryUpdateGestures(new Dictionary<string, string> { [name] = gesture }, out var error))
                _logger.Warn($"Could not register default shortcut {name}: {error}");
        }
    }

    public bool TryUpdateHotkeys(IReadOnlyDictionary<string, string> changes, out string error)
    {
        var previous = _settings.Hotkeys;
        var updated = new Dictionary<string, string>(previous, previous.Comparer);
        foreach (var (name, gesture) in changes)
            updated[name] = gesture;
        if (!_hotkeyService.TryUpdateGestures(changes, out error)) return false;

        _settings.Hotkeys = updated;
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            _settings.Hotkeys = previous;
            var restore = changes.Keys.ToDictionary(name => name, name => previous.GetValueOrDefault(name, DefaultHotkeys[name]));
            if (!_hotkeyService.TryUpdateGestures(restore, out var restoreError))
                _logger.Warn($"Could not roll back shortcuts after save failure: {restoreError}");
            _logger.Error("Could not save shortcuts", ex);
            error = "settings-write-failed";
            return false;
        }
        return true;
    }
}
