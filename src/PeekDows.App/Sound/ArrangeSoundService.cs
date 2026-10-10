using System;
using System.Collections.Generic;
using System.IO;
using PeekDows.Core.Services;
using PeekDows.Core.Win32;

namespace PeekDows.App.Sound;

/// <summary>
/// Plays the arrange-success chime via WinMM MCI (alias "peekdows_arrange").
/// Factory sounds ship under Assets/Sounds and are addressed by bare name
/// (no extension); user sounds live in %APPDATA%\PeekDows\sounds\ and are
/// addressed by file name (with extension). Never throws to the caller.
/// </summary>
public sealed class ArrangeSoundService : IDisposable
{
    private static readonly HashSet<string> FactoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chime-soft", "chime-double", "pop-minimal"
    };

    private const string Alias = "peekdows_arrange";

    private readonly Func<string> _soundsDirProvider;
    private readonly FileLogger? _logger;
    private bool _disposed;

    public ArrangeSoundService(Func<string> soundsDirProvider, FileLogger? logger)
    {
        _soundsDirProvider = soundsDirProvider ?? throw new ArgumentNullException(nameof(soundsDirProvider));
        _logger = logger;
    }

    public void Play(string? soundName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(soundName))
                return;
            var name = soundName.Trim();

            string? path = null;
            if (string.IsNullOrEmpty(Path.GetExtension(name)) && FactoryNames.Contains(name))
            {
                var candidate = Path.Combine(System.AppContext.BaseDirectory, "Assets", "Sounds", name + ".wav");
                if (File.Exists(candidate))
                    path = candidate;
            }
            else
            {
                string dir;
                try
                {
                    dir = _soundsDirProvider();
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"ArrangeSound: sounds dir unavailable: {ex.Message}");
                    return;
                }
                var full = Path.Combine(dir, name);
                if (File.Exists(full))
                    path = full;
            }

            if (path is null)
            {
                _logger?.Warn($"ArrangeSound: sound not found: {name}");
                return;
            }

            try { NativeMethods.mciSendString($"close {Alias}", null, 0, IntPtr.Zero); } catch { }
            int rc = NativeMethods.mciSendString($"open \"{path}\" alias {Alias}", null, 0, IntPtr.Zero);
            if (rc != 0)
            {
                _logger?.Warn($"ArrangeSound: mci open failed ({rc}) for {name}");
                return;
            }
            rc = NativeMethods.mciSendString($"play {Alias}", null, 0, IntPtr.Zero);
            if (rc != 0)
                _logger?.Warn($"ArrangeSound: mci play failed ({rc}) for {name}");
        }
        catch (Exception ex)
        {
            try { _logger?.Warn($"ArrangeSound: play failed: {ex.Message}"); } catch { }
        }
    }

    public void Stop()
    {
        try { NativeMethods.mciSendString($"close {Alias}", null, 0, IntPtr.Zero); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
