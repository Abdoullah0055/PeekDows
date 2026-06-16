using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PeekDows.Core.Win32;

namespace PeekDows.App.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int HOTKEY_ARRANGE_ID = 1;

    private readonly Action _onArrangeNow;
    private readonly HotkeyMessageWindow _messageWindow;
    private bool _isRegistered;
    private bool _disposed;

    public event Action? ArrangeNowRequested;

    public HotkeyService()
    {
        _messageWindow = new HotkeyMessageWindow(this);
        _onArrangeNow = () => ArrangeNowRequested?.Invoke();
    }

    public bool RegisterArrangeHotkey()
    {
        if (_isRegistered) return true;

        uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
        uint vk = NativeMethods.VK_SPACE;

        _isRegistered = NativeMethods.RegisterHotKey(_messageWindow.Handle, HOTKEY_ARRANGE_ID, modifiers, vk);

        return _isRegistered;
    }

    public void UnregisterAll()
    {
        if (_isRegistered && _messageWindow.Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HOTKEY_ARRANGE_ID);
            _isRegistered = false;
        }
    }

    internal void OnHotkeyReceived(int id)
    {
        if (id == HOTKEY_ARRANGE_ID)
        {
            _onArrangeNow();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
