using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekDows.Core.Models;

public sealed class WindowDiff
{
    public IReadOnlyList<ManagedWindow> Added { get; init; } = [];
    public IReadOnlyList<ManagedWindow> Removed { get; init; } = [];
    public IReadOnlyList<ManagedWindow> Current { get; init; } = [];
}
