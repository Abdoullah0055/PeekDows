using PeekDows.App.Settings;

namespace PeekDows.App.Hotkeys;

public sealed partial class HotkeyService
{
    private readonly record struct Gesture(uint Modifiers, uint Key);
    private readonly Dictionary<int, Gesture> _registeredGestures = new();
    private static int IdForName(string name) =>
        name switch { "arrangeNow" => HOTKEY_ARRANGE_ID, "pauseResume" => HOTKEY_PAUSE_ID, _ => -1 };

    internal bool IsRegistered(string name) => _registeredGestures.ContainsKey(IdForName(name));

    public bool TryUpdateGestures(IReadOnlyDictionary<string, string> requested, out string error, Action? commit = null)
    {
        error = "";
        var changes = new Dictionary<int, Gesture>();
        foreach (var (name, text) in requested)
        {
            int id = IdForName(name);
            if (id < 0 || !HotkeyParser.TryParse(text, out var modifiers, out var key))
            {
                error = "bad-gesture";
                return false;
            }

            var gesture = new Gesture(modifiers, key);
            if (!_registeredGestures.TryGetValue(id, out var current) || gesture != current)
                changes.Add(id, gesture);
        }

        var previous = _registeredGestures.Where(entry => changes.ContainsKey(entry.Key)).ToArray();
        var replaced = new List<KeyValuePair<int, Gesture>>();
        foreach (var entry in previous)
        {
            if (!_unregisterHotkey(entry.Key))
            {
                RestoreGestures(replaced);
                error = "hotkey-conflict";
                return false;
            }
            _registeredGestures.Remove(entry.Key);
            replaced.Add(entry);
        }

        var registered = new List<int>();
        foreach (var (id, gesture) in changes)
        {
            if (!RegisterGesture(id, gesture))
            {
                RollBack();
                error = "hotkey-conflict";
                return false;
            }
            registered.Add(id);
        }
        try { commit?.Invoke(); }
        catch
        {
            RollBack();
            throw;
        }
        return true;

        void RollBack()
        {
            foreach (var added in registered)
            {
                if (_unregisterHotkey(added))
                    _registeredGestures.Remove(added);
                else
                    _logger?.Warn($"Could not unregister hotkey id={added} during rollback");
            }
            RestoreGestures(replaced);
        }
    }

    private void RestoreGestures(IEnumerable<KeyValuePair<int, Gesture>> previous)
    {
        foreach (var (id, gesture) in previous)
            if (_registeredGestures.ContainsKey(id) || !RegisterGesture(id, gesture))
                _logger?.Warn($"Could not restore hotkey id={id}");
    }

    private bool RegisterGesture(int id, Gesture gesture)
    {
        if (!_registerHotkey(id, gesture.Modifiers, gesture.Key))
            return false;
        _registeredGestures[id] = gesture;
        return true;
    }
}
