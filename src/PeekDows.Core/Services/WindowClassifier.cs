using System;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class WindowClassifier
{
    private readonly AppSettings _settings;

    public WindowClassifier(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsEligible(RawWindowInfo window)
    {
        if (!window.IsVisible) return false;
        if (window.IsMinimized) return false;
        if (window.IsCloaked) return false;
        if (string.IsNullOrWhiteSpace(window.Title)) return false;
        if (window.CurrentRect.Width < 250 || window.CurrentRect.Height < 180) return false;
        if (window.ProcessId == Environment.ProcessId) return false;
        if (IsIgnoredProcess(window.ProcessName)) return false;
        if (IsIgnoredClass(window.ClassName)) return false;
        if (IsSystemWindow(window)) return false;
        return true;
    }

    public bool IsIgnoredProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var trimmed = processName.Trim();
        return _settings.IgnoredProcesses.Any(p =>
            string.Equals(p?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsIgnoredClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        var trimmed = className.Trim();
        return _settings.IgnoredClasses.Any(c =>
            string.Equals(c?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsSystemWindow(RawWindowInfo window)
    {
        if (window.Title == "Program Manager" && string.Equals(window.ClassName, "Progman", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(window.ClassName, "WorkerW", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(window.ClassName, "NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(window.Title)) return true;

        return false;
    }

    public bool IsFullscreen(RawWindowInfo window, Rect monitorWorkArea)
    {
        if (!window.IsVisible || window.IsMinimized) return false;

        return window.CurrentRect.Left <= monitorWorkArea.Left
            && window.CurrentRect.Top <= monitorWorkArea.Top
            && window.CurrentRect.Right >= monitorWorkArea.Right
            && window.CurrentRect.Bottom >= monitorWorkArea.Bottom;
    }

    public bool IsConsideredFullscreen(bool isMaximized, bool isVisible, bool isMinimized, Rect windowRect, Rect monitorWorkArea)
    {
        if (isMaximized) return false;
        if (!isVisible || isMinimized) return false;

        return windowRect.Left <= monitorWorkArea.Left
            && windowRect.Top <= monitorWorkArea.Top
            && windowRect.Right >= monitorWorkArea.Right
            && windowRect.Bottom >= monitorWorkArea.Bottom;
    }
}
