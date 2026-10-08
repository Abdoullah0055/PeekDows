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
