using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PeekDows.App.Tray;
using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.App.Settings;

/// <summary>
/// WinForms host for the WebView2-based settings UI. Owns the message pump between the
/// UI and the pure SettingsBridge, mirrors external tray changes into the page, and
/// guards against closing with unsaved changes. TryCreate returns null (never throws)
/// when the WebView2 runtime is unavailable so the caller can fall back to the legacy
/// window.
/// </summary>
public sealed class SettingsHostForm : Form
{
    private const string VirtualHost = "app.local";

    private readonly IPeekDowsController _controller;
    private readonly SettingsService _settingsService;
    private readonly FileLogger? _logger;
    private readonly SettingsBridge _bridge;
    private WebView2? _webView;
    private bool _isDirty;

    /// <summary>Raised when WebView2 fails after the host form was created.</summary>
    public event Action<string>? InitializationFailed;

    /// <summary>
    /// Returns a ready-to-show host form, or null when the WebView2 runtime is missing
    /// (caller falls back to the legacy window). Never throws.
    /// </summary>
    public static SettingsHostForm? TryCreate(
        IPeekDowsController controller,
        SettingsService settingsService,
        FileLogger? logger,
        out string? unavailableReason)
    {
        try
        {
            // Cheap synchronous probe — throws when the Evergreen runtime is absent.
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            unavailableReason = null;
            return new SettingsHostForm(controller, settingsService, logger);
        }
        catch (Exception ex)
        {
            unavailableReason = ex.Message;
            return null;
        }
    }

    private SettingsHostForm(IPeekDowsController controller, SettingsService settingsService, FileLogger? logger)
    {
        _controller = controller;
        _settingsService = settingsService;
        _logger = logger;
        _bridge = new SettingsBridge(controller, settingsService, logger, openSettingsFolder: OpenSettingsFolder);
        _bridge.SetWindowsProvider(() => _controller.GetWindowsSnapshot());
        _bridge.SetWindowsMessageHandler((type, root) => WindowsMessageHandler.TryHandle(type, root, () => _controller.GetWindowsSnapshot(), WindowsMessageHandler.CamelCase));
        _bridge.DirtyChanged += OnDirtyChanged;

        Text = "PeekDows Settings";
        Width = 1000;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new System.Drawing.Size(860, 560);

        SubscribeExternalEvents();
        Load += async (_, _) => await InitializeWebViewAsync();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => UnsubscribeExternalEvents();
    }

    private async System.Threading.Tasks.Task InitializeWebViewAsync()
    {
        try
        {
            _logger?.Info("SettingsHostForm: initializing WebView2");
            var environment = await CoreWebView2Environment.CreateAsync();
            _webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_webView);
            await _webView.EnsureCoreWebView2Async(environment);

            var core = _webView.CoreWebView2;
            var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            core.SetVirtualHostNameToFolderMapping(VirtualHost, wwwroot, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;   // no popups
            core.WindowCloseRequested += (_, _) => Close();          // Cancel button in JS
            core.WebMessageReceived += OnWebMessageReceived;

            core.Navigate($"https://{VirtualHost}/index.html");
            _logger?.Info("SettingsHostForm: settings UI loaded");
        }
        catch (Exception ex)
        {
            _logger?.Error("SettingsHostForm: WebView2 initialization failed", ex);
            InitializationFailed?.Invoke(ex.Message);
            Close();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var response = _bridge.HandleMessage(e.WebMessageAsJson);
            if (response != null)
            {
                _webView?.CoreWebView2.PostWebMessageAsJson(response);
                _logger?.Info($"SettingsHostForm: bridge response posted ({response.Length} chars)");
            }
        }
        catch (Exception ex)
        {
            // The bridge never throws by contract; this is a belt-and-braces guard so a
            // malformed post can never take down the UI thread.
            _logger?.Warn($"SettingsHostForm: bridge dispatch failed: {ex.Message}");
        }
    }

    // ----- external changes (tray toggles while the window is open) -----

    private void SubscribeExternalEvents()
    {
        _controller.AutoArrangeChanged += OnExternalChange;
        _controller.AnimateWindowTransitionsChanged += OnExternalChange;
        _controller.DirectionalFocusChanged += OnExternalChange;
        _controller.StartWithWindowsChanged += OnExternalChange;
        _controller.AllowRepositionMaximizedWindowsChanged += OnExternalChange;
        _controller.WindowSizePresetChanged += OnExternalPresetChange;
        _controller.FocusHintModeChanged += OnExternalHintModeChange;
        _controller.StateChanged += OnExternalStateChange;
    }

    private void UnsubscribeExternalEvents()
    {
        _controller.AutoArrangeChanged -= OnExternalChange;
        _controller.AnimateWindowTransitionsChanged -= OnExternalChange;
        _controller.DirectionalFocusChanged -= OnExternalChange;
        _controller.StartWithWindowsChanged -= OnExternalChange;
        _controller.AllowRepositionMaximizedWindowsChanged -= OnExternalChange;
        _controller.WindowSizePresetChanged -= OnExternalPresetChange;
        _controller.FocusHintModeChanged -= OnExternalHintModeChange;
        _controller.StateChanged -= OnExternalStateChange;
        _bridge.DirtyChanged -= OnDirtyChanged;
        InitializationFailed = null;
    }

    private void OnExternalChange(bool _) => PostExternalChange();
    private void OnExternalPresetChange(WindowSizePreset _) => PostExternalChange();
    private void OnExternalHintModeChange(string _) => PostExternalChange();
    private void OnExternalStateChange(RuntimeState _) => PostExternalChange();

    private void PostExternalChange()
    {
        if (_webView?.CoreWebView2 == null) return;
        try
        {
            _webView.CoreWebView2.PostWebMessageAsJson(_bridge.BuildExternalChangeMessage());
        }
        catch (Exception ex)
        {
            _logger?.Warn($"SettingsHostForm: external change post failed: {ex.Message}");
        }
    }

    // ----- dirty close guard -----

    private void OnDirtyChanged(bool dirty)
    {
        _isDirty = dirty;
        _logger?.Info($"SettingsHostForm: dirty state = {dirty}");
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isDirty && e.CloseReason == CloseReason.UserClosing)
        {
            var result = MessageBox.Show(this,
                "You have unsaved changes. Close anyway?",
                "PeekDows", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result == DialogResult.No) e.Cancel = true;
        }
    }

    private void OpenSettingsFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsService.SettingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("SettingsHostForm: failed to open settings folder", ex);
        }
    }
}
