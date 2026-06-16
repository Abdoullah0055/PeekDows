using System;
using System.Collections.Generic;

namespace PeekDows.Core.Models;

public sealed class PlacementResult
{
    public int AttemptedCount { get; init; }
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}
