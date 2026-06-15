using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public class WindowDiscoveryService
{
    private Dictionary<IntPtr, ManagedWindow> _knownWindows = new();

    public IReadOnlyList<RawWindowInfo> GetTopLevelWindows()
    {
        var windows = new List<RawWindowInfo>();

        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            bool isVisible = NativeMethods.IsWindowVisible(hwnd);
            bool isMinimized = NativeMethods.IsIconic(hwnd);
            bool isMaximized = NativeMethods.IsZoomed(hwnd);

            var sbTitle = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sbTitle, sbTitle.Capacity);

            var sbClass = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sbClass, sbClass.Capacity);

            NativeMethods.GetWindowRect(hwnd, out var nativeRect);
            var rect = new Rect(nativeRect.left, nativeRect.top, nativeRect.right - nativeRect.left, nativeRect.bottom - nativeRect.top);

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "";
            try
            {
                using var process = Process.GetProcessById((int)pid);
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

    public IReadOnlyList<ManagedWindow> GetEligibleWindows(WindowClassifier classifier)
    {
        var allWindows = GetTopLevelWindows();
        var now = DateTime.Now;
        var validWindows = new List<ManagedWindow>();

        foreach (var rawInfo in allWindows)
        {
            if (!classifier.IsEligible(rawInfo))
                continue;

            if (!_knownWindows.TryGetValue(rawInfo.Hwnd, out var managed))
            {
                managed = ManagedWindow.FromRaw(rawInfo, now);
                _knownWindows[rawInfo.Hwnd] = managed;
            }
            else
            {
                managed.Title = rawInfo.Title;
                managed.CurrentRect = rawInfo.CurrentRect;
                managed.IsVisible = rawInfo.IsVisible;
                managed.IsMinimized = rawInfo.IsMinimized;
                managed.IsMaximized = rawInfo.IsMaximized;
                managed.IsForeground = rawInfo.IsForeground;
                managed.LastSeenAt = now;
            }

            if (managed.IsForeground)
            {
                managed.LastFocusedAt = now;
            }

            validWindows.Add(managed);
        }

        var deadHwnds = _knownWindows.Keys.Except(validWindows.Select(w => w.Hwnd)).ToList();
        foreach (var deadHwnd in deadHwnds)
        {
            _knownWindows.Remove(deadHwnd);
        }

        return validWindows;
    }

    public WindowDiff CompareWithPreviousSnapshot(IReadOnlyList<ManagedWindow> currentEligible)
    {
        var previousHwnds = new HashSet<IntPtr>(_knownWindows.Keys);
        var currentHwnds = new HashSet<IntPtr>(currentEligible.Select(w => w.Hwnd));

        var added = currentEligible.Where(w => !previousHwnds.Contains(w.Hwnd)).ToList();
        var removed = _knownWindows.Values.Where(w => !currentHwnds.Contains(w.Hwnd)).ToList();

        foreach (var dead in removed)
        {
            _knownWindows.Remove(dead.Hwnd);
        }

        foreach (var win in currentEligible)
        {
            _knownWindows[win.Hwnd] = win;
        }

        return new WindowDiff
        {
            Added = added,
            Removed = removed,
            Current = currentEligible
        };
    }
}
