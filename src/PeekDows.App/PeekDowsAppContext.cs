using System;
using System.Windows.Forms;
using PeekDows.App.Tray;
using PeekDows.Core.Services;

namespace PeekDows.App;

public class PeekDowsAppContext : ApplicationContext
{
    private readonly TrayIconController _trayController;
    private readonly SettingsService _settingsService;

    public PeekDowsAppContext()
    {
        // Initialize Core services
        _settingsService = new SettingsService();
        _settingsService.Load(); // Ensure it creates / loads the config at startup

        // Initialize Tray App
        _trayController = new TrayIconController();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayController.Dispose();
        }
        base.Dispose(disposing);
    }
}
