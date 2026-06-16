using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public sealed class WindowPlacementService
{
    private readonly IWindowPositioner _positioner;
    private readonly Func<IntPtr, bool> _isWindowValid;
    private readonly FileLogger? _logger;

    public WindowPlacementService() : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), null) { }

    public WindowPlacementService(FileLogger logger) : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), logger) { }

    internal WindowPlacementService(IWindowPositioner positioner) : this(positioner, _ => true, null) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid) : this(positioner, isWindowValid, null) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid, FileLogger? logger)
    {
        _positioner = positioner;
        _isWindowValid = isWindowValid;
        _logger = logger;
    }

    public PlacementResult ApplyPlacements(IReadOnlyList<WindowPlacement> placements)
    {
        if (placements == null || placements.Count == 0)
        {
            _logger?.Info("ApplyPlacements: no placements to apply");
            return new PlacementResult
            {
                AttemptedCount = 0,
                SucceededCount = 0,
                FailedCount = 0
            };
        }

        _logger?.Info($"ApplyPlacements started: {placements.Count} placement(s)");

        int succeeded = 0;
        int failed = 0;
        var errors = new List<string>();

        foreach (var placement in placements)
        {
            try
            {
                _logger?.Info($"Attempting placement: hwnd={placement.Hwnd}, slot={placement.SlotId}, rect={placement.TargetRect}");

                if (placement.Hwnd == IntPtr.Zero)
                {
                    failed++;
                    var msg = $"Invalid hwnd for slot {placement.SlotId}";
                    errors.Add(msg);
                    _logger?.Warn($"Placement skipped: {msg}");
                    continue;
                }

                if (!_isWindowValid(placement.Hwnd))
                {
                    failed++;
                    var msg = $"Window no longer exists: hwnd={placement.Hwnd}, slot={placement.SlotId}";
                    errors.Add(msg);
                    _logger?.Warn($"Placement skipped: {msg}");
                    continue;
                }

                if (placement.TargetRect.Width <= 0 || placement.TargetRect.Height <= 0)
                {
                    failed++;
                    var msg = $"Invalid rect for slot {placement.SlotId}: {placement.TargetRect}";
                    errors.Add(msg);
                    _logger?.Warn($"Placement skipped: {msg}");
                    continue;
                }

                bool result = _positioner.SetWindowPosition(placement.Hwnd, placement.TargetRect);
                if (result)
                {
                    succeeded++;
                    _logger?.Info($"SetWindowPos succeeded: hwnd={placement.Hwnd}, slot={placement.SlotId}");
                }
                else
                {
                    failed++;
                    int win32Error = Marshal.GetLastWin32Error();
                    var msg = $"SetWindowPos failed: hwnd={placement.Hwnd}, slot={placement.SlotId}, win32Error={win32Error}";
                    errors.Add(msg);
                    _logger?.Error(msg);
                }
            }
            catch (Exception ex)
            {
                failed++;
                var msg = $"Exception for hwnd={placement.Hwnd}, slot={placement.SlotId}: {ex.Message}";
                errors.Add(msg);
                _logger?.Error($"Placement exception: hwnd={placement.Hwnd}, slot={placement.SlotId}", ex);
            }
        }

        _logger?.Info($"ApplyPlacements completed: attempted={placements.Count}, succeeded={succeeded}, failed={failed}");

        return new PlacementResult
        {
            AttemptedCount = placements.Count,
            SucceededCount = succeeded,
            FailedCount = failed,
            Errors = errors
        };
    }
}
