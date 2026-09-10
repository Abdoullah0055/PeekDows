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
        var now = DateTime.Now;

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

        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            bool isVisible = NativeMethods.IsWindowVisible(hwnd);
            bool isMinimized = NativeMethods.IsIconic(hwnd);
            bool isMaximized = NativeMethods.IsZoomed(hwnd);

            var sbTitle = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sbTitle, sbTitle.Capacity);

            var sbClass = new System.Text.StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sbClass, sbClass.Capacity);

            NativeMethods.GetWindowRect(hwnd, out var nativeRect);
            var rect = new Rect(nativeRect.left, nativeRect.top, nativeRect.right - nativeRect.left, nativeRect.bottom - nativeRect.top);

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "";
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                processName = process.ProcessName + ".exe";
            }
            catch
            {
                processName = "unknown.exe";
            }

            bool isCloaked = false;
            if (MonitorNativeMethods.DwmGetWindowAttribute(hwnd, MonitorNativeMethods.DWMWA_CLOAKED, out int cloakedVal, sizeof(int)) == 0)
            {
                isCloaked = cloakedVal != 0;
            }

            bool isForeground = NativeMethods.GetForegroundWindow() == hwnd;

            windows.Add(new RawWindowInfo
            {
                Hwnd = hwnd,
                Title = sbTitle.ToString(),
                ClassName = sbClass.ToString(),
                ProcessId = (int)pid,
                ProcessName = processName,
                CurrentRect = rect,
                IsVisible = isVisible,
                IsMinimized = isMinimized,
                IsMaximized = isMaximized,
                IsForeground = isForeground,
                IsCloaked = isCloaked
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
