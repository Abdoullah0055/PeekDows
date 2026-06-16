using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PeekDows.Core.Models;
using PeekDows.Core.Win32;

namespace PeekDows.Core.Services;

public sealed class WindowPlacementService
{
    private readonly IWindowPositioner _positioner;
    private readonly Func<IntPtr, bool> _isWindowValid;
    private readonly Func<IntPtr, bool> _isWindowMaximized;
    private readonly FileLogger? _logger;

    public WindowPlacementService() : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), hwnd => NativeMethods.IsZoomed(hwnd), null) { }

    public WindowPlacementService(FileLogger logger) : this(new Win32WindowPositioner(), hwnd => NativeMethods.IsWindow(hwnd), hwnd => NativeMethods.IsZoomed(hwnd), logger) { }

    internal WindowPlacementService(IWindowPositioner positioner) : this(positioner, _ => true, _ => false, null) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid) : this(positioner, isWindowValid, _ => false, null) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid, FileLogger? logger) : this(positioner, isWindowValid, _ => false, logger) { }

    internal WindowPlacementService(IWindowPositioner positioner, Func<IntPtr, bool> isWindowValid, Func<IntPtr, bool> isWindowMaximized, FileLogger? logger)
    {
        _positioner = positioner;
        _isWindowValid = isWindowValid;
        _isWindowMaximized = isWindowMaximized;
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

        var peeks = placements.Where(p => !p.BringToFront).ToList();
        var focus = placements.FirstOrDefault(p => p.BringToFront);

        foreach (var placement in peeks)
        {
            if (TryApplyPlacement(placement, ref succeeded, ref failed, errors)) { }
        }

        if (focus != null)
        {
            TryApplyPlacement(focus, ref succeeded, ref failed, errors);
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

    private bool TryApplyPlacement(WindowPlacement placement, ref int succeeded, ref int failed, List<string> errors)
    {
        try
        {
            _logger?.Info($"Attempting placement: hwnd={placement.Hwnd}, slot={placement.SlotId}, rect={placement.TargetRect}, bringToFront={placement.BringToFront}");

            if (placement.Hwnd == IntPtr.Zero)
            {
                failed++;
                var msg = $"Invalid hwnd for slot {placement.SlotId}";
                errors.Add(msg);
                _logger?.Warn($"Placement skipped: {msg}");
                return false;
            }

            if (!_isWindowValid(placement.Hwnd))
            {
                failed++;
                var msg = $"Window no longer exists: hwnd={placement.Hwnd}, slot={placement.SlotId}";
                errors.Add(msg);
                _logger?.Warn($"Placement skipped: {msg}");
                return false;
            }

            if (placement.TargetRect.Width <= 0 || placement.TargetRect.Height <= 0)
            {
                failed++;
                var msg = $"Invalid rect for slot {placement.SlotId}: {placement.TargetRect}";
                errors.Add(msg);
                _logger?.Warn($"Placement skipped: {msg}");
                return false;
            }

            if (_isWindowMaximized(placement.Hwnd))
            {
                _logger?.Info($"Maximized window detected: hwnd={placement.Hwnd}, slot={placement.SlotId}");
                _logger?.Info($"Restoring maximized window before placement: hwnd={placement.Hwnd}, slot={placement.SlotId}");
                bool restoreResult = NativeMethods.ShowWindow(placement.Hwnd, NativeMethods.SW_RESTORE);
                _logger?.Info($"ShowWindow(SW_RESTORE) result={restoreResult} for hwnd={placement.Hwnd}");
            }

            _logger?.Info($"SetWindowPos called: hwnd={placement.Hwnd}, bringToFront={placement.BringToFront}");
            bool result = _positioner.SetWindowPosition(placement.Hwnd, placement.TargetRect, placement.BringToFront);
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

            return result;
        }
        catch (Exception ex)
        {
            failed++;
            var msg = $"Exception for hwnd={placement.Hwnd}, slot={placement.SlotId}: {ex.Message}";
            errors.Add(msg);
            _logger?.Error($"Placement exception: hwnd={placement.Hwnd}, slot={placement.SlotId}", ex);
            return false;
        }
    }
}
