using System;
using System.IO;

namespace PeekDows.Core.Services;

public static class ArrangeSoundPlanning
{
    public static DateTime? ComputeAutoOffDeadline(DateTime nowUtc, int totalMinutes)
    {
        if (totalMinutes <= 0)
            return null;
        return nowUtc.AddMinutes(totalMinutes);
    }
}

public static class SoundFileValidator
{
    public static readonly long MaxBytes = 5 * 1024 * 1024;

    public static bool HasSupportedExtension(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;
        var ext = Path.GetExtension(fileName);
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(name))
            return string.Empty;
        name = name.Trim();
        if (name.Length == 0)
            return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '-');
        name = name.Trim();
        if (name.Length == 0)
            return string.Empty;
        if (!HasSupportedExtension(name))
            return string.Empty;
        if (name.Length <= 80)
            return name;
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var maxStem = 80 - ext.Length;
        if (maxStem <= 0)
            return name.Substring(0, 80);
        return stem.Substring(0, maxStem) + ext;
    }

    public static string Deduplicate(string dir, string fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName))
                return fileName;
            if (!Directory.Exists(dir))
                return fileName;
            var full = Path.Combine(dir, fileName);
            if (!File.Exists(full))
                return fileName;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (var i = 2; ; i++)
            {
                var candidate = stem + "-" + i + ext;
                if (!File.Exists(Path.Combine(dir, candidate)))
                    return candidate;
            }
        }
        catch
        {
            return fileName;
        }
    }
}
