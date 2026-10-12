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

    private readonly Action _onArrangeNow;
    private readonly Action _onPauseResume;
    private readonly Action _onUndo;
    private readonly HotkeyMessageWindow _messageWindow;
    private readonly FileLogger? _logger;
    private bool _arrangeRegistered;
    private bool _pauseRegistered;
    private bool _undoRegistered;
    private bool _disposed;

    public event Action? ArrangeNowRequested;
    public event Action? PauseResumeRequested;
    public event Action? UndoRequested;

    public HotkeyService() : this(null) { }

    public HotkeyService(FileLogger? logger)
    {
        _logger = logger;
        _messageWindow = new HotkeyMessageWindow(this);
        _onArrangeNow = () => ArrangeNowRequested?.Invoke();
        _onPauseResume = () => PauseResumeRequested?.Invoke();
        _onUndo = () => UndoRequested?.Invoke();
        _logger?.Info("HotkeyService created");
    }

    public bool RegisterArrangeHotkey()
    {
        if (_arrangeRegistered) return true;

        _logger?.Info("RegisterArrangeHotkey called");

        uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
        uint vk = NativeMethods.VK_SPACE;

        _arrangeRegistered = NativeMethods.RegisterHotKey(_messageWindow.Handle, HOTKEY_ARRANGE_ID, modifiers, vk);

        if (_arrangeRegistered)
        {
            _logger?.Info("RegisterHotKey succeeded: Ctrl+Alt+Space");
        }
        else
        {
            int win32Error = Marshal.GetLastWin32Error();
            _logger?.Warn($"RegisterHotKey failed: Ctrl+Alt+Space, win32Error={win32Error}");
        }

        return _arrangeRegistered;
    }

    public bool RegisterPauseHotkey()
    {
        if (_pauseRegistered) return true;

        _logger?.Info("RegisterPauseHotkey called");

        uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
        uint vk = NativeMethods.VK_P;

        _pauseRegistered = NativeMethods.RegisterHotKey(_messageWindow.Handle, HOTKEY_PAUSE_ID, modifiers, vk);

        if (_pauseRegistered)
        {
            _logger?.Info("Ctrl+Alt+P hotkey registered");
        }
        else
        {
            int win32Error = Marshal.GetLastWin32Error();
            _logger?.Warn($"Failed to register Ctrl+Alt+P hotkey. It may already be in use. win32Error={win32Error}");
        }

        return _pauseRegistered;
    }

    public bool RegisterUndoHotkey()
    {
        if (_undoRegistered) return true;

        _logger?.Info("RegisterUndoHotkey called");

        // Hardcodé par décision (pas de passage par les settings Hotkeys) : Ctrl+Win+Z.
        uint modifiers = (uint)(NativeMethods.MOD_CONTROL | NativeMethods.MOD_WIN);
        uint vk = VK_Z;

        _undoRegistered = NativeMethods.RegisterHotKey(_messageWindow.Handle, HOTKEY_UNDO_ID, modifiers, vk);

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
        if (_arrangeRegistered && _messageWindow.Handle != IntPtr.Zero)
        {
            _logger?.Info("UnregisterHotKey called for arrange");
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HOTKEY_ARRANGE_ID);
            _arrangeRegistered = false;
        }

        if (_pauseRegistered && _messageWindow.Handle != IntPtr.Zero)
        {
            _logger?.Info("UnregisterHotKey called for pause");
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HOTKEY_PAUSE_ID);
            _pauseRegistered = false;
        }

        if (_undoRegistered && _messageWindow.Handle != IntPtr.Zero)
        {
            _logger?.Info("UnregisterHotKey called for undo");
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HOTKEY_UNDO_ID);
            _undoRegistered = false;
        }
    }

    internal void OnHotkeyReceived(int id)
    {
        _logger?.Info($"WM_HOTKEY received: id={id}");

        if (id == HOTKEY_ARRANGE_ID)
        {
            _logger?.Info("ArrangeNowRequested event raised");
            _onArrangeNow();
        }
        else if (id == HOTKEY_PAUSE_ID)
        {
            _logger?.Info("PauseResumeRequested event raised");
            _onPauseResume();
        }
        else if (id == HOTKEY_UNDO_ID)
        {
            _logger?.Info("UndoRequested event raised");
            _onUndo();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger?.Info("HotkeyService disposed");
        UnregisterAll();
        _messageWindow.ForceDestroy();
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
