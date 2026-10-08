using PeekDows.App.Hotkeys;
using PeekDows.App.Settings;

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
