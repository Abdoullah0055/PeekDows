using System;

namespace PeekDows.Core.Models;

public sealed class MonitorInfo
{
    public IntPtr Handle { get; init; }
    public Rect WorkArea { get; init; }
    public Rect FullArea { get; init; }
    public bool IsPrimary { get; init; }

    /// <summary>
    /// True when this <see cref="MonitorInfo"/> is a synthetic fallback produced because the
    /// real monitor could not be resolved (e.g. <c>GetMonitorInfo</c> failed). Layout and
    /// Directional Focus decisions must never rely on a fallback monitor — a fallback would
    /// collapse unrelated windows into one fake monitor bucket. Consumers check this before
    /// using the WorkArea for a placement/registry mapping.
    /// </summary>
    public bool IsFallback { get; init; }
}
