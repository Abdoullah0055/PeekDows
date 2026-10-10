using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Focus;

/// <summary>
/// Resolves and caches per-application icons for window handles (HWND).
/// Bitmaps returned by <see cref="GetEntry"/> are owned by the service — do NOT dispose them.
/// Call <see cref="Clear"/> to drop the cache (entries may go stale: HWNDs are recycled by
/// Windows) and <see cref="Dispose"/> to release all native resources.
/// </summary>
public sealed class WindowIconService : IDisposable
{
    private readonly Func<IntPtr, (Icon? Icon, string Label)>? _customExtractor;
    private readonly FileLogger? _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, (Bitmap? Bitmap, string Label)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public WindowIconService(Func<IntPtr, (Icon? Icon, string Label)>? extractor = null, FileLogger? logger = null)
    {
        _customExtractor = extractor;
        _logger = logger;
    }

    /// <summary>
    /// Returns the cached (Bitmap, Label) entry for <paramref name="hwnd"/>.
    /// The Bitmap is owned by the service and must NOT be disposed by the caller.
    /// Label is the process name or "?". Never throws: any failure yields (null, "?").
    /// </summary>
    public (Bitmap? Icon, string Label) GetEntry(IntPtr hwnd)
    {
        lock (_lock)
        {
            if (_disposed)
                return (null, "?");
            try
            {
                if (_customExtractor is not null)
                    return GetEntryCustom(hwnd);
                return GetEntryDefault(hwnd);
            }
            catch (Exception ex)
            {
                try { _logger?.Error("WindowIconService.GetEntry failed", ex); } catch { }
                return (null, "?");
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                foreach (var entry in _cache.Values)
                {
                    try { entry.Bitmap?.Dispose(); } catch { }
                }
                _cache.Clear();
            }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                foreach (var entry in _cache.Values)
                {
                    try { entry.Bitmap?.Dispose(); } catch { }
                }
                _cache.Clear();
            }
            catch { }
        }
    }

    private (Bitmap? Icon, string Label) GetEntryCustom(IntPtr hwnd)
    {
        // No exe path is known with an injected extractor: key by HWND.
        // NOTE: HWNDs are recycled by Windows — entries may go stale; Clear() drops them.
        string key = $"hwnd:{hwnd}";
        if (_cache.TryGetValue(key, out var hit))
            return hit;
        Icon? icon = null;
        string label = "?";
        try
        {
            (icon, label) = _customExtractor!(hwnd);
        }
        catch
        {
            return CacheAndReturn(key, null, "?");
        }
        if (string.IsNullOrEmpty(label))
            label = "?";
        Bitmap? bmp = ToBitmapAndRelease(ref icon);
        return CacheAndReturn(key, bmp, label);
    }

    private (Bitmap? Icon, string Label) GetEntryDefault(IntPtr hwnd)
    {
        string? exePath = null;
        string label = "?";
        try
        {
            exePath = ResolveExePath(hwnd, out label);
        }
        catch
        {
            exePath = null;
            label = "?";
        }
        if (string.IsNullOrEmpty(label))
            label = "?";
        string key = exePath ?? $"hwnd:{hwnd}";
        if (_cache.TryGetValue(key, out var hit))
            return hit;
        Icon? icon = null;
        try
        {
            icon = ExtractDefault(hwnd, exePath);
        }
        catch
        {
            icon = null;
        }
        Bitmap? bmp = ToBitmapAndRelease(ref icon);
        return CacheAndReturn(key, bmp, label);
    }

    private (Bitmap? Icon, string Label) CacheAndReturn(string key, Bitmap? bmp, string label)
    {
        var entry = (bmp, label);
        try { _cache[key] = entry; } catch { }
        return entry;
    }

    /// <summary>
    /// Converts <paramref name="icon"/> to a Bitmap once, then disposes the source Icon.
    /// Returns null if there is no icon or the conversion fails. Never throws.
    /// </summary>
    private static Bitmap? ToBitmapAndRelease(ref Icon? icon)
    {
        Bitmap? bmp = null;
        try
        {
            bmp = icon?.ToBitmap();
        }
        catch
        {
            bmp = null;
        }
        finally
        {
            try { icon?.Dispose(); } catch { }
            icon = null;
        }
        return bmp;
    }

    private static string? ResolveExePath(IntPtr hwnd, out string label)
    {
        label = "?";
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return null;
            string? path;
            try
            {
                using var proc = Process.GetProcessById(unchecked((int)pid));
                path = proc.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrEmpty(path))
                return null;
            try { label = Path.GetFileNameWithoutExtension(path); } catch { label = "?"; }
            if (string.IsNullOrEmpty(label))
                label = "?";
            return path;
        }
        catch
        {
            label = "?";
            return null;
        }
    }

    private static Icon? ExtractDefault(IntPtr hwnd, string? exePath)
    {
        // 2. WM_GETICON via non-blocking SendMessageTimeout (200ms): SMALL2 first, then BIG.
        try
        {
            IntPtr h = SendMessageIcon(hwnd, NativeMethods.ICON_SMALL2);
            if (h == IntPtr.Zero)
                h = SendMessageIcon(hwnd, NativeMethods.ICON_BIG);
            if (h != IntPtr.Zero)
            {
                var owned = CloneShared(h);
                if (owned is not null)
                    return owned;
            }
        }
        catch { }
        // 3. Class icons (shared handles — NEVER DestroyIcon): small first, then big.
        try
        {
            ulong sm = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICONSM).ToUInt64();
            if (sm != 0)
            {
                var owned = CloneShared(unchecked((IntPtr)(long)sm));
                if (owned is not null)
                    return owned;
            }
            ulong big = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICON).ToUInt64();
            if (big != 0)
            {
                var owned = CloneShared(unchecked((IntPtr)(long)big));
                if (owned is not null)
                    return owned;
            }
        }
        catch { }
        // 4. ExtractIconEx from the exe — prefer large (better downscale at high DPI).
        if (!string.IsNullOrEmpty(exePath))
        {
            try { return ExtractFromFile(exePath); } catch { }
        }
        // 5. Total failure → null (caller pairs it with the resolved label).
        return null;
    }

    private static IntPtr SendMessageIcon(IntPtr hwnd, int iconType)
    {
        try
        {
            IntPtr res = NativeMethods.SendMessageTimeout(
                hwnd,
                NativeMethods.WM_GETICON,
                (IntPtr)iconType,
                IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG,
                200,
                out IntPtr hIcon);
            if (res == IntPtr.Zero)
                return IntPtr.Zero; // timed out or hung — treat as failure
            return hIcon;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Clones a shared (window/class) icon handle into an owned copy.
    /// The source handle is NEVER destroyed here — it belongs to the window/class.
    /// </summary>
    private static Icon? CloneShared(IntPtr hIcon)
    {
        try
        {
            using var shared = Icon.FromHandle(hIcon);
            return (Icon)shared.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static Icon? ExtractFromFile(string exePath)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        try
        {
            uint count;
            try
            {
                count = NativeMethods.ExtractIconEx(exePath, 0, large, small, 1);
            }
            catch
            {
                return null;
            }
            if (count == 0)
                return null;
            IntPtr h = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (h == IntPtr.Zero)
                return null;
            return CloneShared(h);
        }
        finally
        {
            // Handles from ExtractIconEx are OURS — destroy them
            // (the clone above owns its own copy).
            try { if (large[0] != IntPtr.Zero) NativeMethods.DestroyIcon(large[0]); } catch { }
            try { if (small[0] != IntPtr.Zero) NativeMethods.DestroyIcon(small[0]); } catch { }
        }
    }
}
