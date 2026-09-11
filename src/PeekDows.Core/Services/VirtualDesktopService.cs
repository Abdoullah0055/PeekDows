using System;
using System.Runtime.InteropServices;
using PeekDows.Core.Services;

namespace PeekDows.Core.Services;

public interface IVirtualDesktopService
{
    bool IsWindowOnCurrentVirtualDesktop(IntPtr hwnd);
}

public sealed class VirtualDesktopService : IVirtualDesktopService
{
    // P-A4/P-C7 fix: lazy COM + TTL cache per-hwnd (cold COM ~0.2ms each, avoid N calls).
    private const int CacheTtlMs = 800;
    private readonly Lazy<IVirtualDesktopManager?> _lazyManager;
    private IVirtualDesktopManager? _injectedManager;
    private bool _usesInjected;
    private readonly FileLogger? _logger;
    private bool _comUnavailableLogged;
    private int _comInitTried;
    private readonly Dictionary<IntPtr, (bool Value, long Ticks)> _cache = new();
    private readonly object _cacheLock = new();

    public VirtualDesktopService(FileLogger? logger = null)
    {
        _logger = logger;
        _lazyManager = new Lazy<IVirtualDesktopManager?>(CreateManager);
    }

    private IVirtualDesktopManager? CreateManager()
    {
        if (System.Threading.Interlocked.Exchange(ref _comInitTried, 1) == 1) { }
        try
        {
            var m = (IVirtualDesktopManager?)new CVirtualDesktopManager();
            _logger?.Info("VirtualDesktopService: IVirtualDesktopManager COM instance created (lazy)");
            return m;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"VirtualDesktopService: COM unavailable, virtual desktop filtering disabled: {ex.Message}");
            return null;
        }
    }

    private IVirtualDesktopManager? Manager => _usesInjected ? _injectedManager : _lazyManager.Value;

    internal VirtualDesktopService(IVirtualDesktopManager? manager, FileLogger? logger = null)
    {
        _injectedManager = manager;
        _usesInjected = true;
        _lazyManager = new Lazy<IVirtualDesktopManager?>(() => manager);
        _logger = logger;
    }

    public bool IsWindowOnCurrentVirtualDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        // P-A4: TTL cache (800ms) — batch of 40 wins with same hwnd across classifier+snapshot hits cache.
        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long ttlTicks = CacheTtlMs * System.Diagnostics.Stopwatch.Frequency / 1000;
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(hwnd, out var entry) && (nowTicks - entry.Ticks) < ttlTicks)
                return entry.Value;
        }

        var manager = Manager;
        if (manager == null)
        {
            if (!_comUnavailableLogged)
            {
                _comUnavailableLogged = true;
                _logger?.Warn("VirtualDesktopService: IsWindowOnCurrentVirtualDesktop returning true (COM unavailable)");
            }
            return true;
        }

        bool result;
        try
        {
            int hr = manager.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrentDesktop);
            if (hr == 0) result = onCurrentDesktop;
            else
            {
                _logger?.Warn($"VirtualDesktopService: IsWindowOnCurrentVirtualDesktop returned hr=0x{hr:X8} for hwnd={hwnd}, falling back to true");
                result = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"VirtualDesktopService: IsWindowOnCurrentVirtualDesktop exception for hwnd={hwnd}: {ex.Message}, falling back to true");
            result = true;
        }
        lock (_cacheLock) _cache[hwnd] = (result, nowTicks);
        return result;
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVirtualDesktopManager
    {
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    internal class CVirtualDesktopManager
    {
    }
}
