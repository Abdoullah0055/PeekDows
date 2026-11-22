using System.IO;
using PeekDows.App.Startup;
using PeekDows.Core.Models;

namespace PeekDows.App.Settings;

internal delegate bool RegisterSettingsHotkeys(
    IReadOnlyDictionary<string, string> changes, out string error, Action commit);

internal static class SettingsCommitter
{
    internal static bool TryCommit(
        AppSettings previous,
        AppSettings next,
        IStartupService startup,
        RegisterSettingsHotkeys register,
        Action<AppSettings> save,
        Action<AppSettings> publish,
        Func<bool> isPaused,
        Action arrange,
        Action<string, Exception> log,
        out string error)
    {
        var changedHotkeys = new Dictionary<string, string>();
        foreach (var (name, fallback) in PeekDowsAppContext.DefaultHotkeys)
        {
            var before = previous.Hotkeys.GetValueOrDefault(name, fallback);
            var after = next.Hotkeys.GetValueOrDefault(name, fallback);
            if (before != after && !HotkeyParser.AreEquivalent(before, after))
                changedHotkeys.Add(name, after);
        }

        string failure = "hotkey-update-failed";
        try
        {
            if (!register(changedHotkeys, out error, Persist))
                return false;
        }
        catch (Exception ex)
        {
            log(failure, ex);
            error = failure;
            return false;
        }

        try
        {
            publish(next);
            bool shouldArrange = previous.LayoutMode != next.LayoutMode
                || previous.WindowSizePreset != next.WindowSizePreset;
            if (shouldArrange && next.Enabled && !isPaused())
                arrange();
        }
        catch (Exception ex)
        {
            log("Could not apply settings to the running app", ex);
        }

        error = "";
        return true;

        void Persist()
        {
            bool startupChanged = previous.StartWithWindows != next.StartWithWindows;
            failure = "startup-update-failed";
            var originalShortcut = startupChanged ? startup.CaptureShortcut() : null;

            try
            {
                if (startupChanged && !startup.SetEnabled(next.StartWithWindows))
                    throw new IOException("Could not update the Startup shortcut.");

                failure = "settings-write-failed";
                save(next);
            }
            catch
            {
                if (startupChanged)
                {
                    try { startup.RestoreShortcut(originalShortcut); }
                    catch (Exception ex) { log("Could not restore the Startup shortcut", ex); }
                }
                throw;
            }
        }
    }
}
