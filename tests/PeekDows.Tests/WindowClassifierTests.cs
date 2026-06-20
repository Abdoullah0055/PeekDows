using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Services;
using Xunit;

namespace PeekDows.Tests;

public class WindowClassifierTests
{
    private readonly AppSettings _settings;
    private readonly WindowClassifier _classifier;

    public WindowClassifierTests()
    {
        _settings = new AppSettings();
        _classifier = new WindowClassifier(_settings);
    }

    private RawWindowInfo MakeWindow(
        IntPtr hwnd = default,
        string title = "Test Window",
        string className = "Chrome_WidgetWin_1",
        string processName = "chrome.exe",
        bool isVisible = true,
        bool isMinimized = false,
        bool isMaximized = false,
        bool isForeground = false,
        bool isCloaked = false,
        int width = 800,
        int height = 600,
        int processId = 1234)
    {
        return new RawWindowInfo
        {
            Hwnd = hwnd == default ? (IntPtr)1 : hwnd,
            Title = title,
            ClassName = className,
            ProcessId = processId,
            ProcessName = processName,
            CurrentRect = new Rect(0, 0, width, height),
            IsVisible = isVisible,
            IsMinimized = isMinimized,
            IsMaximized = isMaximized,
            IsForeground = isForeground,
            IsCloaked = isCloaked
        };
    }

    [Fact]
    public void NormalApp_IsEligible()
    {
        var window = MakeWindow(processName: "chrome.exe", className: "Chrome_WidgetWin_1");

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void EmptyTitle_IsIgnored()
    {
        var window = MakeWindow(title: "");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void WhitespaceTitle_IsIgnored()
    {
        var window = MakeWindow(title: "   ");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Invisible_IsIgnored()
    {
        var window = MakeWindow(isVisible: false);

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Minimized_IsIgnored()
    {
        var window = MakeWindow(isMinimized: true);

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Cloaked_IsIgnored()
    {
        var window = MakeWindow(isCloaked: true);

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void SmallWindow_IsIgnored()
    {
        var window = MakeWindow(width: 200, height: 150);

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void SmallWidth_IsIgnored()
    {
        var window = MakeWindow(width: 200, height: 600);

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void SmallHeight_IsIgnored()
    {
        var window = MakeWindow(width: 800, height: 100);

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void PeekDows_IsIgnored()
    {
        var window = MakeWindow(processName: "PeekDows.exe");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void IgnoredProcess_IsIgnored()
    {
        var window = MakeWindow(processName: "SearchHost.exe");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void IgnoredClass_IsIgnored()
    {
        var window = MakeWindow(className: "Shell_TrayWnd");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Desktop_ProgramManager_IsIgnored()
    {
        var window = MakeWindow(title: "Program Manager", className: "Progman");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Taskbar_ShellTrayWnd_IsIgnored()
    {
        var window = MakeWindow(className: "Shell_TrayWnd", title: "");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void WorkerW_IsIgnored()
    {
        var window = MakeWindow(className: "WorkerW");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void IsIgnoredProcess_WithMatchingProcess()
    {
        Assert.True(_classifier.IsIgnoredProcess("SearchHost.exe"));
    }

    [Fact]
    public void IsIgnoredProcess_CaseInsensitive()
    {
        Assert.True(_classifier.IsIgnoredProcess("searchhost.exe"));
    }

    [Fact]
    public void IsIgnoredClass_WithMatchingClass()
    {
        Assert.True(_classifier.IsIgnoredClass("Shell_TrayWnd"));
    }

    [Fact]
    public void IsIgnoredClass_CaseInsensitive()
    {
        Assert.True(_classifier.IsIgnoredClass("shell_traywnd"));
    }

    [Fact]
    public void IsIgnoredProcess_NormalApp_NotIgnored()
    {
        Assert.False(_classifier.IsIgnoredProcess("chrome.exe"));
    }

    [Fact]
    public void FileExplorer_IsEligible()
    {
        var window = MakeWindow(processName: "explorer.exe", className: "CabinetWClass", title: "Documents");

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void VSCode_IsEligible()
    {
        var window = MakeWindow(processName: "Code.exe", className: "Chrome_WidgetWin_1", title: "project - Visual Studio Code");

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void Terminal_IsEligible()
    {
        var window = MakeWindow(processName: "WindowsTerminal.exe", className: "WindowClass", title: "Terminal");

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void Notepad_IsEligible()
    {
        var window = MakeWindow(processName: "Notepad.exe", className: "Notepad", title: "notes.txt - Notepad");

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void Edge_IsEligible()
    {
        var window = MakeWindow(processName: "msedge.exe", className: "Chrome_WidgetWin_1", title: "Google - Microsoft Edge");

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void IsSystemWindow_Desktop()
    {
        var window = MakeWindow(title: "Program Manager", className: "Progman");

        Assert.True(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void IsSystemWindow_Taskbar()
    {
        var window = MakeWindow(className: "Shell_TrayWnd");

        Assert.True(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void IsSystemWindow_NormalApp()
    {
        var window = MakeWindow(className: "Chrome_WidgetWin_1");

        Assert.False(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void IsFullscreen_TrueWhenCoversWorkArea()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var window = MakeWindow(width: 1920, height: 1080);

        Assert.True(_classifier.IsFullscreen(window, workArea));
    }

    [Fact]
    public void IsFullscreen_FalseWhenSmaller()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var window = MakeWindow(width: 1440, height: 720);

        Assert.False(_classifier.IsFullscreen(window, workArea));
    }

    [Fact]
    public void CustomIgnoredProcess_IsIgnored()
    {
        var customSettings = new AppSettings();
        customSettings.IgnoredProcesses.Add("MyApp.exe");
        var classifier = new WindowClassifier(customSettings);
        var window = MakeWindow(processName: "MyApp.exe");

        Assert.False(classifier.IsEligible(window));
    }

    [Fact]
    public void CustomIgnoredClass_IsIgnored()
    {
        var customSettings = new AppSettings();
        customSettings.IgnoredClasses.Add("MyCustomClass");
        var classifier = new WindowClassifier(customSettings);
        var window = MakeWindow(className: "MyCustomClass");

        Assert.False(classifier.IsEligible(window));
    }

    [Fact]
    public void ApplicationFrameWindow_EmptyTitle_IsIgnored()
    {
        var window = MakeWindow(className: "ApplicationFrameWindow", title: "");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void IsConsideredFullscreen_MaximizedWindowCoveringWorkArea_IsNotSkipped()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var windowRect = new Rect(-7, -7, 1934, 1094);

        Assert.False(_classifier.IsConsideredFullscreen(
            isMaximized: true, isVisible: true, isMinimized: false,
            windowRect: windowRect, monitorWorkArea: workArea));
    }

    [Fact]
    public void IsConsideredFullscreen_NonMaximizedCoveringWorkArea_IsSkipped()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var windowRect = new Rect(-7, -7, 1934, 1094);

        Assert.True(_classifier.IsConsideredFullscreen(
            isMaximized: false, isVisible: true, isMinimized: false,
            windowRect: windowRect, monitorWorkArea: workArea));
    }

    [Fact]
    public void IsConsideredFullscreen_NormalWindow_NotSkipped()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var windowRect = new Rect(0, 0, 960, 540);

        Assert.False(_classifier.IsConsideredFullscreen(
            isMaximized: false, isVisible: true, isMinimized: false,
            windowRect: windowRect, monitorWorkArea: workArea));
    }

    [Fact]
    public void Classifier_IgnoresProcess_CaseInsensitive()
    {
        var window = MakeWindow(processName: "searchhost.exe");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Classifier_IgnoresClass_CaseInsensitive()
    {
        var window = MakeWindow(className: "shell_traywnd");

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Classifier_DoesNotIgnoreExplorerProcessByDefault()
    {
        Assert.False(_classifier.IsIgnoredProcess("explorer.exe"));
    }

    [Fact]
    public void Classifier_IgnoresShellTrayWnd()
    {
        Assert.True(_classifier.IsIgnoredClass("Shell_TrayWnd"));
    }

    [Fact]
    public void Classifier_IgnoresWorkerW()
    {
        Assert.True(_classifier.IsIgnoredClass("WorkerW"));
    }

    [Fact]
    public void Classifier_IgnoresProgman()
    {
        Assert.True(_classifier.IsIgnoredClass("Progman"));
    }

    [Fact]
    public void Classifier_IgnoresCurrentProcessWindow()
    {
        var window = MakeWindow(processId: Environment.ProcessId);

        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void Classifier_DoesNotCrash_WhenProcessNameNullOrEmpty()
    {
        Assert.False(_classifier.IsIgnoredProcess(null));
        Assert.False(_classifier.IsIgnoredProcess(""));
        Assert.False(_classifier.IsIgnoredProcess("   "));
    }

    [Fact]
    public void Classifier_DoesNotCrash_WhenClassNameNullOrEmpty()
    {
        Assert.False(_classifier.IsIgnoredClass(null));
        Assert.False(_classifier.IsIgnoredClass(""));
        Assert.False(_classifier.IsIgnoredClass("   "));
    }

    [Fact]
    public void Classifier_AllowsNormalFileExplorerWindow()
    {
        var window = MakeWindow(
            processName: "explorer.exe",
            className: "CabinetWClass",
            title: "Downloads",
            isVisible: true,
            isMinimized: false);

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void Classifier_IgnoresProcessWithTrimmedValue()
    {
        var settings = new AppSettings();
        settings.IgnoredProcesses = new List<string> { "  MyCustomApp.exe  " };
        var classifier = new WindowClassifier(settings);

        Assert.True(classifier.IsIgnoredProcess("MyCustomApp.exe"));
    }

    [Fact]
    public void Classifier_IgnoresClassWithTrimmedValue()
    {
        var settings = new AppSettings();
        settings.IgnoredClasses = new List<string> { "  MyCustomClass  " };
        var classifier = new WindowClassifier(settings);

        Assert.True(classifier.IsIgnoredClass("MyCustomClass"));
    }

    [Fact]
    public void Classifier_IgnoresWindowsUICoreWindow()
    {
        Assert.True(_classifier.IsIgnoredClass("Windows.UI.Core.CoreWindow"));
    }

    [Fact]
    public void IsSystemWindow_CaseInsensitive()
    {
        var window = MakeWindow(className: "shell_traywnd");

        Assert.True(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void Classifier_IgnoresSystemSettingsProcess()
    {
        Assert.True(_classifier.IsIgnoredProcess("SystemSettings.exe"));
    }

    [Fact]
    public void Classifier_IgnoresDV2ControlHost()
    {
        Assert.True(_classifier.IsIgnoredClass("DV2ControlHost"));
    }

    [Fact]
    public void Classifier_AlwaysIgnoresShellTrayWnd_EvenIfRemovedFromSettings()
    {
        var settings = new AppSettings();
        settings.IgnoredClasses.Clear();
        var classifier = new WindowClassifier(settings);

        Assert.True(classifier.IsIgnoredClass("Shell_TrayWnd"));
    }

    [Fact]
    public void Classifier_AlwaysIgnoresSearchHost_EvenIfRemovedFromSettings()
    {
        var settings = new AppSettings();
        settings.IgnoredProcesses.Clear();
        var classifier = new WindowClassifier(settings);

        Assert.True(classifier.IsIgnoredProcess("SearchHost.exe"));
    }

    [Fact]
    public void Classifier_AllowsUserToRemoveNonMandatoryIgnoredProcess()
    {
        var settings = new AppSettings();
        settings.IgnoredProcesses.Clear();
        settings.IgnoredProcesses.Add("MyApp.exe");
        var classifier = new WindowClassifier(settings);

        settings.IgnoredProcesses.Clear();

        Assert.False(classifier.IsIgnoredProcess("MyApp.exe"));
    }

    [Fact]
    public void Classifier_UpdateSettings_AppliesNewSettings()
    {
        var settings = new AppSettings();
        var classifier = new WindowClassifier(settings);

        Assert.False(classifier.IsIgnoredProcess("MyApp.exe"));

        settings.IgnoredProcesses.Add("MyApp.exe");
        classifier.UpdateSettings(settings);

        Assert.True(classifier.IsIgnoredProcess("MyApp.exe"));
    }

    [Fact]
    public void Classifier_UserIgnoredProcess_StillIgnoredWhenAddedToSettings()
    {
        var settings = new AppSettings();
        settings.IgnoredProcesses.Add("Notepad.exe");
        var classifier = new WindowClassifier(settings);

        Assert.True(classifier.IsIgnoredProcess("Notepad.exe"));
    }

    [Fact]
    public void Classifier_UserIgnoredClass_StillIgnoredWhenAddedToSettings()
    {
        var settings = new AppSettings();
        settings.IgnoredClasses.Add("MyAppWindowClass");
        var classifier = new WindowClassifier(settings);

        Assert.True(classifier.IsIgnoredClass("MyAppWindowClass"));
    }

    // --- Near-fullscreen non-maximized eligibility (regression tests for the bug where
    //     PeekDows skipped windows that covered the work area even though they were not
    //     truly maximized). Eligibility is governed by window STATE, not by rect size. ---

    [Fact]
    public void NonMaximizedNearFullscreenWindow_IsEligible()
    {
        // A window manually dragged/resized to nearly fill a 1920x1080 work area, but NOT
        // maximized. PeekDows must treat it as a normal arrangeable window.
        var window = MakeWindow(
            isMaximized: false,
            width: 1900,
            height: 1060);

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void NonMaximizedWindowCoveringWorkArea_IsEligible()
    {
        // Covers the entire work area (oversized like a maximized window's rect) but the
        // genuine maximized state is false. Must still be eligible.
        var window = new RawWindowInfo
        {
            Hwnd = (IntPtr)42,
            Title = "Big Window",
            ClassName = "Chrome_WidgetWin_1",
            ProcessName = "chrome.exe",
            ProcessId = 1234,
            CurrentRect = new Rect(-7, -7, 1934, 1094),
            IsVisible = true,
            IsMinimized = false,
            IsMaximized = false,
            IsForeground = false,
            IsCloaked = false
        };

        Assert.True(_classifier.IsEligible(window));
    }

    [Fact]
    public void MaximizedWindow_IsStillEligible_ForArrangeDecisionInController()
    {
        // IsEligible must not reject a genuinely maximized window; the controller decides
        // skip-vs-arrange based on AllowRepositionMaximizedWindows. A maximized window is
        // otherwise a normal top-level app window.
        var window = MakeWindow(isMaximized: true, width: 1920, height: 1040);

        Assert.True(_classifier.IsEligible(window));
    }

    // --- Hardening: Windows shell overlays (Task View / Alt-Tab) must never be arranged. ---

    [Fact]
    public void XamlExplorerHostIslandWindow_IsNotEligible()
    {
        // The Windows 11 Task View / Alt-Tab overlay host class.
        var window = MakeWindow(className: "XamlExplorerHostIslandWindow", processName: "explorer.exe", title: "Changement de tâche");
        Assert.False(_classifier.IsEligible(window));
    }

    [Fact]
    public void TaskSwitchingTitle_IsSystemWindow()
    {
        var window = MakeWindow(title: "Changement de tâche");
        Assert.True(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void TaskSwitchingEnglishTitle_IsSystemWindow()
    {
        var window = MakeWindow(title: "Task Switching");
        Assert.True(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void TaskViewTitle_IsSystemWindow()
    {
        var window = MakeWindow(title: "Task View");
        Assert.True(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void XamlExplorerHostIslandWindowClass_Alone_IsSystemWindow()
    {
        // Defensive: matched on class even without the localised title, so a future Windows
        // build with a different overlay title is still caught.
        var window = MakeWindow(className: "XamlExplorerHostIslandWindow", title: "Some Other Title");
        Assert.True(_classifier.IsSystemWindow(window));
    }

    [Fact]
    public void NormalApp_StillEligible_AfterOverlayExclusions()
    {
        // Regression guard: the new exclusions must not over-match normal app windows.
        var window = MakeWindow(processName: "quicknote.exe", className: "Tauri Window", title: "Notes");
        Assert.True(_classifier.IsEligible(window));
        Assert.False(_classifier.IsSystemWindow(window));
    }
}
