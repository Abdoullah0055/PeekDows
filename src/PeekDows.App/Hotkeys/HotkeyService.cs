using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int HOTKEY_ARRANGE_ID = 1;

    private readonly Action _onArrangeNow;
    private readonly HotkeyMessageWindow _messageWindow;
    private readonly FileLogger? _logger;
    private bool _isRegistered;
    private bool _disposed;

    public event Action? ArrangeNowRequested;

    public HotkeyService() : this(null) { }

    public HotkeyService(FileLogger? logger)
    {
        _logger = logger;
        _messageWindow = new HotkeyMessageWindow(this);
        _onArrangeNow = () => ArrangeNowRequested?.Invoke();
        _logger?.Info("HotkeyService created");
    }

    public bool RegisterArrangeHotkey()
    {
        if (_isRegistered) return true;

        _logger?.Info("RegisterArrangeHotkey called");

        uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
        uint vk = NativeMethods.VK_SPACE;

        _isRegistered = NativeMethods.RegisterHotKey(_messageWindow.Handle, HOTKEY_ARRANGE_ID, modifiers, vk);

        if (_isRegistered)
        {
            _logger?.Info("RegisterHotKey succeeded: Ctrl+Alt+Space");
        }
        else
        {
            int win32Error = Marshal.GetLastWin32Error();
            _logger?.Warn($"RegisterHotKey failed: Ctrl+Alt+Space, win32Error={win32Error}");
        }

        return _isRegistered;
    }

    public void UnregisterAll()
    {
        if (_isRegistered && _messageWindow.Handle != IntPtr.Zero)
        {
            _logger?.Info("UnregisterHotKey called");
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HOTKEY_ARRANGE_ID);
            _isRegistered = false;
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
