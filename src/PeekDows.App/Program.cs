using System;
using System.Linq;
using System.Windows.Forms;
using PeekDows.App.Startup;
using PeekDows.Core.Services;

namespace PeekDows.App;

static class Program
{
    // Lightweight installer hook: "PeekDows.App.exe --enable-startup" creates the
    // Startup folder shortcut (via the existing StartupService) and flips the
    // StartWithWindows setting, WITHOUT starting the tray app. This keeps the
    // installer free of a duplicated shortcut mechanism and guarantees the tray
    // menu checkmark, the settings file and the Startup shortcut all agree.
    private const string EnableStartupArg = "--enable-startup";

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains(EnableStartupArg, StringComparer.OrdinalIgnoreCase))
        {
            EnableStartupAndExit();
            return;
        }

        // ApplicationConfiguration applies the csproj settings (notably
        // ApplicationHighDpiMode=PerMonitorV2): without it the process stays
        // DPI-unaware and Windows bitmap-scales the whole window on scaled
        // displays (>100%), making the UI look blurry. It also enables visual
        // styles, so the two legacy calls below are no longer needed.
        ApplicationConfiguration.Initialize();

        using (var context = new PeekDowsAppContext())
        {
            Application.Run(context);
        }
    }

    private static void EnableStartupAndExit()
    {
        var logger = new FileLogger();
        try
        {
            logger.Info("--enable-startup: enabling Start with Windows");

            var settingsService = new SettingsService();
            var settings = settingsService.Load();

            var startup = new StartupService(logger);
            var ok = startup.Enable();
            if (!ok)
            {
                logger.Warn("--enable-startup: StartupService.Enable() returned false");
            }

            // Keep the settings flag in sync with the shortcut so the tray menu
            // shows the right checkmark on the first real launch.
            settings.StartWithWindows = ok;
            settingsService.Save(settings);

            logger.Info($"--enable-startup: done, enabled={ok}");
        }
        catch (Exception ex)
        {
            // Swallow: this runs from the installer post-step; never show a dialog.
            logger.Error("--enable-startup: failed", ex);
        }
    }
}
