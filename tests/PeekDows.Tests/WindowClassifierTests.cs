using System;
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
        int height = 600)
    {
        return new RawWindowInfo
        {
            Hwnd = hwnd == default ? (IntPtr)1 : hwnd,
            Title = title,
            ClassName = className,
            ProcessId = 1234,
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
}
