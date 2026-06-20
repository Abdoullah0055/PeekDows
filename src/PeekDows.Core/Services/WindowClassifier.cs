using System;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class WindowClassifier
{
    private AppSettings _settings;
    private readonly Func<IntPtr, bool> _isOnCurrentVirtualDesktop;

    private static readonly string[] AlwaysIgnoredProcessNames =
    {
        "SearchHost.exe",
        "StartMenuExperienceHost.exe",
        "ShellExperienceHost.exe",
        "TextInputHost.exe",
        "LockApp.exe"
    };

    private static readonly string[] AlwaysIgnoredClassNames =
    {
        "Shell_TrayWnd",
        "WorkerW",
        "Progman",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "DV2ControlHost",
        "Windows.UI.Composition.DesktopWindowContentBridge",
        // Windows 11 Task View / Alt-Tab overlay host. These are transient shell surfaces
        // (e.g. "Changement de tâche" / "Task Switching") that must never be arranged or
        // focused — arranging them corrupts the shell and can hang explorer.exe.
        "XamlExplorerHostIslandWindow"
    };

    public WindowClassifier(AppSettings settings) : this(settings, _ => true) { }

    public WindowClassifier(AppSettings settings, Func<IntPtr, bool> isOnCurrentVirtualDesktop)
    {
        _settings = settings;
        _isOnCurrentVirtualDesktop = isOnCurrentVirtualDesktop;
    }

    public void UpdateSettings(AppSettings settings)
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
        if (!_isOnCurrentVirtualDesktop(window.Hwnd)) return false;
        return true;
    }

    public bool IsIgnoredProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var trimmed = processName.Trim();

        if (AlwaysIgnoredProcessNames.Any(p =>
            string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase)))
            return true;

        return _settings.IgnoredProcesses?.Any(p =>
            string.Equals(p?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    public bool IsIgnoredClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        var trimmed = className.Trim();

        if (AlwaysIgnoredClassNames.Any(c =>
            string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)))
            return true;

        return _settings.IgnoredClasses?.Any(c =>
            string.Equals(c?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    public bool IsSystemWindow(RawWindowInfo window)
    {
        if (window.Title == "Program Manager" && string.Equals(window.ClassName, "Progman", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(window.ClassName, "WorkerW", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(window.ClassName, "NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(window.Title)) return true;

        // Windows 11 Task View / Alt-Tab overlays. Match defensively on BOTH class name
        // (XamlExplorerHostIslandWindow, also caught by AlwaysIgnoredClassNames) and the
        // localised title, because the title is what shows in the logs and the class can vary
        // across Windows builds. These are never real arrange/focus targets.
        if (string.Equals(window.ClassName, "XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase)) return true;

        if (!string.IsNullOrWhiteSpace(window.Title))
        {
            var title = window.Title.Trim();
            if (string.Equals(title, "Changement de tâche", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(title, "Task Switching", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(title, "Task View", StringComparison.OrdinalIgnoreCase)) return true;
        }

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
