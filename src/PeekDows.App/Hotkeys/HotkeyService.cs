using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Hotkeys;

public sealed partial class HotkeyService : IDisposable
{
    private const int HOTKEY_ARRANGE_ID = 1;
    private const int HOTKEY_PAUSE_ID = 2;
    private const int HOTKEY_UNDO_ID = 3;
    private const uint VK_Z = 0x5A;

    private readonly HotkeyMessageWindow? _messageWindow;
    private readonly Func<int, uint, uint, bool> _registerHotkey;
    private readonly Func<int, bool> _unregisterHotkey;
    private readonly FileLogger? _logger;
    private bool _undoRegistered;
    private bool _disposed;

    public event Action? ArrangeNowRequested;
    public event Action? PauseResumeRequested;
    public event Action? UndoRequested;

    public HotkeyService(FileLogger? logger)
    {
        _logger = logger;
        var window = new HotkeyMessageWindow(this);
        _messageWindow = window;
        _registerHotkey = (id, modifiers, key) => NativeMethods.RegisterHotKey(window.Handle, id, modifiers, key);
        _unregisterHotkey = id => NativeMethods.UnregisterHotKey(window.Handle, id);
        _logger?.Info("HotkeyService created");
    }

    internal HotkeyService(Func<int, uint, uint, bool> registerHotkey, Func<int, bool> unregisterHotkey, FileLogger? logger = null)
    {
        _logger = logger;
        _registerHotkey = registerHotkey;
        _unregisterHotkey = unregisterHotkey;
    }

    public HotkeyService() : this(null) { }

    public bool RegisterUndoHotkey()
    {
        if (_undoRegistered) return true;

        _logger?.Info("RegisterUndoHotkey called");

        // Hardcodé par décision (pas de passage par les settings Hotkeys) : Ctrl+Win+Z.
        uint modifiers = (uint)(NativeMethods.MOD_CONTROL | NativeMethods.MOD_WIN);
        uint vk = VK_Z;

        _undoRegistered = _registerHotkey(HOTKEY_UNDO_ID, modifiers, vk);

        if (_undoRegistered)
        {
            _logger?.Info("RegisterHotKey succeeded: Ctrl+Win+Z");
        }
        else
        {
            int win32Error = Marshal.GetLastWin32Error();
            _logger?.Warn($"RegisterHotKey failed: Ctrl+Win+Z, win32Error={win32Error}");
        }

        return _undoRegistered;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredGestures.Keys)
            _unregisterHotkey(id);
        _registeredGestures.Clear();

        if (_undoRegistered)
        {
            _logger?.Info("UnregisterHotKey called for undo");
            _unregisterHotkey(HOTKEY_UNDO_ID);
            _undoRegistered = false;
        }
    }

    internal void OnHotkeyReceived(int id)
    {
        _logger?.Info($"WM_HOTKEY received: id={id}");

        if (id == HOTKEY_ARRANGE_ID)
        {
            _logger?.Info("ArrangeNowRequested event raised");
            ArrangeNowRequested?.Invoke();
        }
        else if (id == HOTKEY_PAUSE_ID)
        {
            _logger?.Info("PauseResumeRequested event raised");
            PauseResumeRequested?.Invoke();
        }
        else if (id == HOTKEY_UNDO_ID)
        {
            _logger?.Info("UndoRequested event raised");
            UndoRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger?.Info("HotkeyService disposed");
        UnregisterAll();
        _messageWindow?.ForceDestroy();
    }

    private sealed class HotkeyMessageWindow : NativeWindow
    {
        private readonly HotkeyService _owner;

        public HotkeyMessageWindow(HotkeyService owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams
            {
                Parent = new IntPtr(-3),
                Caption = "PeekDowsHotkeyListener"
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                _owner.OnHotkeyReceived(id);
            }

            base.WndProc(ref m);
        }

        public void ForceDestroy()
        {
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
