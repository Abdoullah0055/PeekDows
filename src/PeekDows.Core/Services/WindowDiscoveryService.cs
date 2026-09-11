using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public class WindowDiscoveryService
{
    private Dictionary<IntPtr, ManagedWindow> _knownWindows = new();
    private readonly Func<IReadOnlyList<RawWindowInfo>> _windowSource;

    public WindowDiscoveryService() : this(DefaultWindowSource) { }

    internal WindowDiscoveryService(Func<IReadOnlyList<RawWindowInfo>> windowSource)
    {
        _windowSource = windowSource;
    }

    public IReadOnlyList<RawWindowInfo> GetTopLevelWindows()
    {
        return _windowSource();
    }

    public WindowDiff Refresh(WindowClassifier classifier)
    {
        var rawWindows = _windowSource();
        var now = DateTime.UtcNow;

        var previousSnapshot = new Dictionary<IntPtr, ManagedWindow>(_knownWindows);

        var currentEligible = new List<ManagedWindow>();

        foreach (var raw in rawWindows)
        {
            if (!classifier.IsEligible(raw))
                continue;

            if (previousSnapshot.TryGetValue(raw.Hwnd, out var existing))
            {
                // C1 fix: HWND reuse — if process changed, treat as a brand new window
                // so stale FirstSeenAt/Pinned/AssignedSlot don't carry over.
                if (existing.ProcessId != raw.ProcessId)
                {
                    var fresh = ManagedWindow.FromRaw(raw, now);
                    if (fresh.IsForeground) fresh.LastFocusedAt = now;
                    currentEligible.Add(fresh);
                }
                else
                {
                    existing.Title = raw.Title;
                    existing.CurrentRect = raw.CurrentRect;
                    existing.IsVisible = raw.IsVisible;
                    existing.IsMinimized = raw.IsMinimized;
                    existing.IsMaximized = raw.IsMaximized;
                    existing.IsForeground = raw.IsForeground;
                    existing.LastSeenAt = now;

                    if (existing.IsForeground)
                    {
                        existing.LastFocusedAt = now;
                    }

                    currentEligible.Add(existing);
                }
            }
            else
            {
                var managed = ManagedWindow.FromRaw(raw, now);
                if (managed.IsForeground)
                {
                    managed.LastFocusedAt = now;
                }

                currentEligible.Add(managed);
            }
        }

        var previousHwnds = new HashSet<IntPtr>(previousSnapshot.Keys);
        var currentHwnds = new HashSet<IntPtr>(currentEligible.Select(w => w.Hwnd));

        var added = currentEligible.Where(w => !previousHwnds.Contains(w.Hwnd)).ToList();
        var removed = previousSnapshot.Values.Where(w => !currentHwnds.Contains(w.Hwnd)).ToList();

        _knownWindows = currentEligible.ToDictionary(w => w.Hwnd);

        return new WindowDiff
        {
            Added = added,
            Removed = removed,
            Current = currentEligible
        };
    }

    public IReadOnlyList<ManagedWindow> GetKnownWindows() => _knownWindows.Values.ToList();

    private static IReadOnlyList<RawWindowInfo> DefaultWindowSource()
    {
        var windows = new List<RawWindowInfo>();
        // P-C2 fix: cache foreground once instead of per-window.
        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        var pidNameCache = new Dictionary<uint, string>();
        // P-B1/P-C1 optimization: reuse StringBuilders and defer GetWindowText/GetClassName/DWM until fast filters pass.
        // We still need rect/pid for that decision, so fetch cheap Win32 first.
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            bool isVisible = NativeMethods.IsWindowVisible(hwnd);
            if (!isVisible) return true;
            bool isMinimized = NativeMethods.IsIconic(hwnd);
            if (isMinimized) return true;

            // P-C1: fast DWM check deferred — but still needed for IsEligible. Call here to avoid title/class alloc for cloaked.
            bool isCloaked = false;
            if (MonitorNativeMethods.DwmGetWindowAttribute(hwnd, MonitorNativeMethods.DWMWA_CLOAKED, out int cloakedVal, sizeof(int)) == 0)
            {
                isCloaked = cloakedVal != 0;
                if (isCloaked) return true;
            }

            var sbTitle = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sbTitle, sbTitle.Capacity);
            string title = sbTitle.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            NativeMethods.GetWindowRect(hwnd, out var nativeRect);
            var rect = new Rect(nativeRect.left, nativeRect.top, nativeRect.right - nativeRect.left, nativeRect.bottom - nativeRect.top);
            if (rect.Width < 250 || rect.Height < 180) return true;

            var sbClass = new System.Text.StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pidNameCache.TryGetValue(pid, out string? processName))
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                    processName = process.ProcessName + ".exe";
                }
                catch { processName = "unknown.exe"; }
                pidNameCache[pid] = processName;
            }

            bool isMaximized = NativeMethods.IsZoomed(hwnd);
            bool isForeground = fgHwnd == hwnd;

            windows.Add(new RawWindowInfo
            {
                Hwnd = hwnd,
                Title = title,
                ClassName = className,
                ProcessId = (int)pid,
                ProcessName = processName,
                CurrentRect = rect,
                IsVisible = true,
                IsMinimized = false,
                IsMaximized = isMaximized,
                IsForeground = isForeground,
                IsCloaked = false
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
