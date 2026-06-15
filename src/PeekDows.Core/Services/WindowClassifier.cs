using System;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class WindowClassifier
{
    private readonly AppSettings _settings;

    private static readonly string[] SystemClassNames =
    {
        "Shell_TrayWnd",
        "WorkerW",
        "Progman",
        "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow",
        "ApplicationFrameWindow"
    };

    private static readonly string[] SystemProcessNames =
    {
        "SearchHost.exe",
        "StartMenuExperienceHost.exe",
        "ShellExperienceHost.exe",
        "TextInputHost.exe",
        "LockApp.exe"
    };

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
        if (window.ProcessName.Equals("PeekDows.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsIgnoredClass(window.ClassName)) return false;
        if (IsIgnoredProcess(window.ProcessName)) return false;
        if (IsSystemWindow(window)) return false;
        return true;
    }

    public bool IsIgnoredProcess(string processName)
    {
        return _settings.IgnoredProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsIgnoredClass(string className)
    {
        return _settings.IgnoredClasses.Contains(className, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSystemWindow(RawWindowInfo window)
    {
        if (window.Title == "Program Manager" && window.ClassName == "Progman") return true;

        if (window.ClassName == "Shell_TrayWnd") return true;
        if (window.ClassName == "WorkerW") return true;
        if (window.ClassName == "NotifyIconOverflowWindow") return true;

        if (window.ClassName == "Windows.UI.Core.CoreWindow") return true;

        if (window.ClassName == "ApplicationFrameWindow" && string.IsNullOrWhiteSpace(window.Title)) return true;

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
}
