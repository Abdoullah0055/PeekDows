using System;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class WindowClassifier
{
    // P-D3 fix: O(1) lookup for ignored lists. Rebuilt on ctor + UpdateSettings + on live mutation.
    // Perf: EnsureCacheFresh is O(1) fast-path (reference + Count) — bench showed ComputeHash per
    // IsEligible (100 windows × 500 iters) made classifier 7× slower when hashing every call.
    private HashSet<string> _ignoredProcessSet = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _ignoredClassSet = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string>? _cachedProcessListRef;
    private IReadOnlyList<string>? _cachedClassListRef;
    private int _cachedProcessCount;
    private int _cachedClassCount;
    private int _cachedProcessHash;
    private int _cachedClassHash;
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
        RebuildIgnoredSets();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        RebuildIgnoredSets();
    }

    private void RebuildIgnoredSets()
    {
        var procSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in AlwaysIgnoredProcessNames)
            procSet.Add(p);
        if (_settings.IgnoredProcesses != null)
            foreach (var p in _settings.IgnoredProcesses)
                if (!string.IsNullOrWhiteSpace(p)) procSet.Add(p.Trim());
        _ignoredProcessSet = procSet;
        _cachedProcessListRef = _settings.IgnoredProcesses;
        _cachedProcessCount = _settings.IgnoredProcesses?.Count ?? 0;
        _cachedProcessHash = ComputeHash(_settings.IgnoredProcesses);

        var clsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in AlwaysIgnoredClassNames)
            clsSet.Add(c);
        if (_settings.IgnoredClasses != null)
            foreach (var c in _settings.IgnoredClasses)
                if (!string.IsNullOrWhiteSpace(c)) clsSet.Add(c.Trim());
        _ignoredClassSet = clsSet;
        _cachedClassListRef = _settings.IgnoredClasses;
        _cachedClassCount = _settings.IgnoredClasses?.Count ?? 0;
        _cachedClassHash = ComputeHash(_settings.IgnoredClasses);
    }

    private static int ComputeHash(IReadOnlyList<string>? list)
    {
        if (list == null || list.Count == 0) return 0;
        int h = 17;
        foreach (var s in list)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            h = h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(s.Trim());
        }
        h = h * 31 + list.Count;
        return h;
    }

    private void EnsureProcessCacheFresh()
    {
        var list = _settings.IgnoredProcesses;
        // Fast O(1) path: reference + Count unchanged => assume fresh (covers 99.9% of IsEligible calls).
        // This fixes the bench regression where ComputeHash per call made Classifier 7× slower.
        // Live mutation via Clear/AddRange changes Count and triggers rebuild; direct indexer
        // mutation with same Count requires UpdateSettings() — documented edge case.
        if (ReferenceEquals(list, _cachedProcessListRef) && (list?.Count ?? 0) == _cachedProcessCount)
            return;
        // Count or reference changed => rebuild (counts as dirty). Also handle hash collision
        // for same-Count content swap by checking hash lazily only on this slow path.
        int cur = ComputeHash(list);
        if (cur != _cachedProcessHash) RebuildIgnoredSets();
        else
        {
            // Hash same but reference/count changed (e.g. new list with same content) — just refresh refs.
            _cachedProcessListRef = list;
            _cachedProcessCount = list?.Count ?? 0;
        }
    }

    private void EnsureClassCacheFresh()
    {
        var list = _settings.IgnoredClasses;
        if (ReferenceEquals(list, _cachedClassListRef) && (list?.Count ?? 0) == _cachedClassCount)
            return;
        int cur = ComputeHash(list);
        if (cur != _cachedClassHash) RebuildIgnoredSets();
        else
        {
            _cachedClassListRef = list;
            _cachedClassCount = list?.Count ?? 0;
        }
    }

    public bool IsEligible(RawWindowInfo window)
    {
        if (!window.IsVisible) return false;
        if (window.IsMinimized) return false;
        if (window.IsCloaked) return false;
        if (string.IsNullOrWhiteSpace(window.Title)) return false;
        if (window.CurrentRect.Width < 250 || window.CurrentRect.Height < 180) return false;
        // C2 fix: self-filter via ProcessId OR tray host class/title so "dotnet run"
        // (parent is dotnet.exe) and double-instance cases still exclude our own windows.
        if (window.ProcessId == Environment.ProcessId) return false;
        if (string.Equals(window.ClassName, "PeekDowsTray", StringComparison.OrdinalIgnoreCase)) return false;
        // Focus hint overlay (Ctrl+Shift gesture): never arrangeable, including from a
        // second PeekDows instance with a different PID (title match is PID-independent).
        if (string.Equals(window.Title, "PeekDowsHintOverlay", StringComparison.Ordinal)) return false;
        if (string.Equals(window.Title, "PeekDows", StringComparison.OrdinalIgnoreCase)
            && string.Equals(window.ClassName, "WindowsForms10.Window", StringComparison.OrdinalIgnoreCase)) return false;
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
        EnsureProcessCacheFresh();
        return _ignoredProcessSet.Contains(trimmed);
    }

    public bool IsIgnoredClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        var trimmed = className.Trim();
        EnsureClassCacheFresh();
        return _ignoredClassSet.Contains(trimmed);
    }

    public bool IsSystemWindow(RawWindowInfo window)
    {
        if (window.Title == "Program Manager" && string.Equals(window.ClassName, "Progman", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(window.ClassName, "WorkerW", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(window.ClassName, "NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(window.ClassName, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(window.Title)) return true;

        // B4 fix: Task View / Alt-Tab overlays — class is the stable signal (localised
        // titles vary per OS language / build and must NOT drive filtering).
        if (string.Equals(window.ClassName, "XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase)) return true;

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
