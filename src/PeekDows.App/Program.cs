using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PeekDows.App.Startup;
using PeekDows.Core.Services;

namespace PeekDows.App;

static class Program
{
    // Initialize Start with Windows on a clean install using the same shortcut
    // service as the tray app. The installer hook exits before starting the tray.
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
        // Single instance: a second copy (dev build + installed build, double
        // tray icon click, ...) would fight over hotkeys, gestures and the
        // overlay, producing flicker and focus chaos. Exit silently.
        using (var mutex = new System.Threading.Mutex(true, @"Global\PeekDows_SingleInstance", out bool createdNew))
        {
            if (!createdNew)
                return;

            ApplicationConfiguration.Initialize();

            using (var context = new PeekDowsAppContext())
            {
                Application.Run(context);
            }
        }
    }

    internal static void EnableStartupAndExit(
        SettingsService? settingsService = null, IStartupService? startup = null, FileLogger? logger = null)
    {
        logger ??= new FileLogger();
        try
        {
            settingsService ??= new SettingsService();
            if (File.Exists(settingsService.SettingsFilePath))
                return;

            logger.Info("--enable-startup: enabling Start with Windows");

            var settings = settingsService.Load();

            startup ??= new StartupService(logger);
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
