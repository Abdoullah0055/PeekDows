using System;
using System.Runtime.InteropServices;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

/// <summary>
/// Sends the benign ALT key pulse used to unlock the Windows foreground lock so
/// <c>SetForegroundWindow</c> is granted. Abstraction exists so unit tests can verify the
/// activation path without invoking the real <c>SendInput</c>.
/// </summary>
public interface IInputSimulator
{
    /// <summary>
    /// Sends one ALT key-down / key-up pulse. Returns true if the pulse was delivered.
    /// Implementations try <c>SendInput</c> first and fall back to <c>keybd_event</c> if that
    /// fails, logging the outcome either way.
    /// </summary>
    bool TryAltPulse();
}

/// <summary>
/// Production input simulator. Primary path is <see cref="NativeMethods.SendInput"/> with the
/// canonical 40-byte INPUT struct; if SendInput rejects the input (e.g. struct size mismatch
/// on an edge build, UIPI blocking, etc.) it falls back to the legacy
/// <see cref="NativeMethods.keybd_event"/>, which modern Windows routes through SendInput
/// internally with a tolerant call shape. Neither path blocks, touches the target window's
/// thread, or uses AttachThreadInput / SetFocus.
/// </summary>
public sealed class Win32InputSimulator : IInputSimulator
{
    private readonly FileLogger? _logger;

    public Win32InputSimulator(FileLogger? logger = null)
    {
        _logger = logger;
    }

    public bool TryAltPulse()
    {
        int cbSize = Marshal.SizeOf<NativeMethods.INPUT>();
        var inputs = new NativeMethods.INPUT[2];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].U.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = (ushort)NativeMethods.VK_MENU,
            dwFlags = 0 // keydown
        };
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].U.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = (ushort)NativeMethods.VK_MENU,
            dwFlags = NativeMethods.KEYEVENTF_KEYUP
        };

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, cbSize);
        int error = Marshal.GetLastWin32Error();
        _logger?.Info($"Activation foreground unlock: SendInput sent={sent}/2, cbSize={cbSize}, win32Error={error}");

        if (sent == inputs.Length)
        {
            return true;
        }

        // Fallback: keybd_event. Legacy but defensive — it tolerates a simpler call shape and
        // modern Windows routes it through SendInput internally. Better than leaving the
        // foreground lock active and getting only a taskbar flash.
        _logger?.Warn($"Activation foreground unlock: SendInput incomplete ({sent}/2), falling back to keybd_event");
        NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, 0, IntPtr.Zero);       // ALT down
        NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero); // ALT up
        _logger?.Info("Activation foreground unlock: keybd_event fallback issued");
        // keybd_event has no return code; assume the pulse was delivered.
        return true;
    }
}
