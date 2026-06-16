using System;
using System.Collections.Generic;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public sealed class WindowPlacementService
{
    private readonly IWindowPositioner _positioner;
    private readonly Func<IntPtr, bool> _isWindowValid;

    public WindowPlacementService() : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd)) { }

    internal WindowPlacementService(IWindowPositioner positioner) : this(positioner, _ => true) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid)
    {
        _positioner = positioner;
        _isWindowValid = isWindowValid;
    }

    public PlacementResult ApplyPlacements(IReadOnlyList<WindowPlacement> placements)
    {
        if (placements == null || placements.Count == 0)
        {
            return new PlacementResult
            {
                AttemptedCount = 0,
                SucceededCount = 0,
                FailedCount = 0
            };
        }

        int succeeded = 0;
        int failed = 0;
        var errors = new List<string>();

        foreach (var placement in placements)
        {
            try
            {
                if (placement.Hwnd == IntPtr.Zero)
                {
                    failed++;
                    errors.Add($"Invalid hwnd for slot {placement.SlotId}");
                    continue;
                }

                if (!_isWindowValid(placement.Hwnd))
                {
                    failed++;
                    errors.Add($"Window no longer exists: hwnd={placement.Hwnd}, slot={placement.SlotId}");
                    continue;
                }

                if (placement.TargetRect.Width <= 0 || placement.TargetRect.Height <= 0)
                {
                    failed++;
                    errors.Add($"Invalid rect for slot {placement.SlotId}: {placement.TargetRect}");
                    continue;
                }

                bool result = _positioner.SetWindowPosition(placement.Hwnd, placement.TargetRect);
                if (result)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    errors.Add($"SetWindowPos failed for hwnd={placement.Hwnd}, slot={placement.SlotId}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Exception for hwnd={placement.Hwnd}, slot={placement.SlotId}: {ex.Message}");
            }
        }

        return new PlacementResult
        {
            AttemptedCount = placements.Count,
            SucceededCount = succeeded,
            FailedCount = failed,
            Errors = errors
        };
    }
}
