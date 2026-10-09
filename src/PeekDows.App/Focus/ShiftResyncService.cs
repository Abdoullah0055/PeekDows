using System;
using System.Diagnostics;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

/// <summary>
/// Clears a phantom (stuck-down) Shift/Ctrl state on the foreground thread's
/// keyboard state without synthesising any input (no SendInput/keybd_event).
/// Call from a WORKER thread, NOT the UI thread: the WM_NULL responsiveness
/// probe blocks up to 200ms on a hung foreground thread, and AttachThreadInput
/// synchronises with that thread. Never throws — all failures return false.
/// </summary>
public static class ShiftResyncService
{
    private const uint ProbeTimeoutMs = 200;
    private const int MaxProcNameChars = 32;

    public static bool TryResyncForeground(FileLogger? logger)
    {
        try
        {
            IntPtr hwnd;
            try
            {
                hwnd = NativeMethods.GetForegroundWindow();
            }
            catch
            {
                return false;
            }
            if (hwnd == IntPtr.Zero)
                return false;

            // Race guard: if Shift/Ctrl is PHYSICALLY held right now (user
            // re-pressed for typing), there is no phantom to clear — clearing
            // would eat their real modifier. Async state reads hardware.
            try
            {
                if (IsPhysicallyDown(NativeMethods.VK_SHIFT)
                    || IsPhysicallyDown(NativeMethods.VK_LSHIFT)
                    || IsPhysicallyDown(NativeMethods.VK_RSHIFT)
                    || IsPhysicallyDown(NativeMethods.VK_CONTROL)
                    || IsPhysicallyDown(NativeMethods.VK_LCONTROL)
                    || IsPhysicallyDown(NativeMethods.VK_RCONTROL))
                {
                    logger?.Info($"ShiftResync skipped (modifiers physically held) fg=\"{ForegroundProcName(hwnd)}\"");
                    return false;
                }
            }
            catch
            {
            }

            // Responsiveness probe: never attach to a hung thread.
            bool responsive;
            try
            {
                IntPtr res = NativeMethods.SendMessageTimeout(
                    hwnd,
                    NativeMethods.WM_NULL,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG,
                    ProbeTimeoutMs,
                    out _);
                responsive = res != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            if (!responsive)
            {
                logger?.Warn($"ShiftResync skipped (foreground hung) fg=\"{ForegroundProcName(hwnd)}\"");
                return false;
            }

            uint fgTid;
            try
            {
                fgTid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            }
            catch
            {
                return false;
            }
            uint ourTid;
            try
            {
                ourTid = NativeMethods.GetCurrentThreadId();
            }
            catch
            {
                return false;
            }

            bool attached;
            try
            {
                attached = NativeMethods.AttachThreadInput(ourTid, fgTid, true);
            }
            catch
            {
                return false;
            }
            if (!attached)
            {
                logger?.Warn($"ShiftResync attach failed fg=\"{ForegroundProcName(hwnd)}\"");
                return false;
            }
            try
            {
                var state = new byte[256];
                if (!NativeMethods.GetKeyboardState(state))
                {
                    logger?.Warn($"ShiftResync GetKeyboardState failed fg=\"{ForegroundProcName(hwnd)}\"");
                    return false;
                }
                // Release the phantom Shift/Ctrl holds (clear the 0x80 down-bit).
                state[NativeMethods.VK_SHIFT] = (byte)(state[NativeMethods.VK_SHIFT] & ~0x80);
                state[NativeMethods.VK_LSHIFT] = (byte)(state[NativeMethods.VK_LSHIFT] & ~0x80);
                state[NativeMethods.VK_RSHIFT] = (byte)(state[NativeMethods.VK_RSHIFT] & ~0x80);
                state[NativeMethods.VK_CONTROL] = (byte)(state[NativeMethods.VK_CONTROL] & ~0x80);
                state[NativeMethods.VK_LCONTROL] = (byte)(state[NativeMethods.VK_LCONTROL] & ~0x80);
                state[NativeMethods.VK_RCONTROL] = (byte)(state[NativeMethods.VK_RCONTROL] & ~0x80);
                if (!NativeMethods.SetKeyboardState(state))
                {
                    logger?.Warn($"ShiftResync SetKeyboardState failed fg=\"{ForegroundProcName(hwnd)}\"");
                    return false;
                }
            }
            finally
            {
                try
                {
                    NativeMethods.AttachThreadInput(ourTid, fgTid, false);
                }
                catch
                {
                    // Detach must never throw.
                }
            }

            logger?.Info($"ShiftResync applied fg=\"{ForegroundProcName(hwnd)}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPhysicallyDown(int vk)
    {
        try
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ForegroundProcName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string name;
            try
            {
                name = Process.GetProcessById((int)pid).ProcessName ?? "?";
            }
            catch
            {
                return "?";
            }
            name = name.Replace('"', '\'');
            if (name.Length > MaxProcNameChars)
                name = name.Substring(0, MaxProcNameChars);
            return name;
        }
        catch
        {
            return "?";
        }
    }
}
