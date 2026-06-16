using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class LayoutEngine
{
    public IReadOnlyList<WindowPlacement> CalculateFocusPeekPlacements(
        IReadOnlyList<ManagedWindow> windows,
        Rect workArea,
        AppSettings settings)
    {
        var result = new List<WindowPlacement>();
        if (windows.Count == 0) return result;

        var priorityWindows = PrioritizeForFocusPeek(windows);

        int horizontalPeek = Math.Clamp((int)(workArea.Width * 0.07), 64, 110);
        int verticalPeek = Math.Clamp((int)(workArea.Height * 0.08), 44, 80);
        int bottomPeek = Math.Clamp((int)(workArea.Height * 0.09), 50, 90);

        var focusRect = new Rect(
            workArea.Left + horizontalPeek,
            workArea.Top + verticalPeek,
            workArea.Width - (horizontalPeek * 2),
            workArea.Height - verticalPeek - bottomPeek
        );

        if (priorityWindows.Count == 1)
        {
            Rect targetRect;
            string slotId;

            if (settings.SingleWindowMode != "TopLeftSlot")
            {
                targetRect = focusRect;
                slotId = "Focus";
            }
            else
            {
                var grid = CreateDefaultGrid(workArea);
                var slots = CreateDefaultSlots(grid);
                targetRect = CalculateSlotRect(slots.First(s => s.Id == "A"), grid);
                slotId = "A";
            }

            result.Add(new WindowPlacement
            {
                Hwnd = priorityWindows[0].Hwnd,
                SlotId = slotId,
                TargetRect = targetRect,
                BringToFront = true
            });

            return result;
        }

        var focusWindow = priorityWindows[0];
        var peekWindows = priorityWindows.Skip(1).Take(4).ToList();

        var leftPeekRect = new Rect(
            workArea.Left - focusRect.Width + horizontalPeek,
            focusRect.Top,
            focusRect.Width,
            focusRect.Height
        );

        var rightPeekRect = new Rect(
            workArea.Right - horizontalPeek,
            focusRect.Top,
            focusRect.Width,
            focusRect.Height
        );

        var bottomPeekRect = new Rect(
            focusRect.Left,
            workArea.Bottom - bottomPeek,
            focusRect.Width,
            focusRect.Height
        );

        var topPeekRect = new Rect(
            focusRect.Left,
            workArea.Top - focusRect.Height + verticalPeek,
            focusRect.Width,
            focusRect.Height
        );

        Rect[] peekRects = [leftPeekRect, rightPeekRect, bottomPeekRect, topPeekRect];
        string[] peekSlotIds = ["PeekLeft", "PeekRight", "PeekBottom", "PeekTop"];

        for (int i = 0; i < peekWindows.Count; i++)
        {
            result.Add(new WindowPlacement
            {
                Hwnd = peekWindows[i].Hwnd,
                SlotId = peekSlotIds[i],
                TargetRect = peekRects[i],
                BringToFront = false
            });
        }

        result.Add(new WindowPlacement
        {
            Hwnd = focusWindow.Hwnd,
            SlotId = "Focus",
            TargetRect = focusRect,
            BringToFront = true
        });

        return result;
    }

    private IReadOnlyList<ManagedWindow> PrioritizeForFocusPeek(IReadOnlyList<ManagedWindow> windows)
    {
        return windows
            .OrderByDescending(w => w.IsPinned)
            .ThenByDescending(w => w.IsForeground)
            .ThenByDescending(w => w.LastFocusedAt ?? DateTime.MinValue)
            .ThenByDescending(w => w.CurrentRect.Width * w.CurrentRect.Height)
            .Take(5)
            .ToList();
    }

    public GridSpec CreateDefaultGrid(Rect workArea)
    {
        return new GridSpec
        {
            WorkArea = workArea,
            Columns = 4,
            Rows = 3,
            CellWidth = workArea.Width / 4,
            CellHeight = workArea.Height / 3
        };
    }

    public IReadOnlyList<SlotDefinition> CreateDefaultSlots(GridSpec grid)
    {
        return new List<SlotDefinition>
        {
            new SlotDefinition { Id = "A", Name = "TopLeft", StartColumn = 0, StartRow = 0, ColumnSpan = 3, RowSpan = 2, CellNumbers = [1, 2, 3, 5, 6, 7] },
            new SlotDefinition { Id = "B", Name = "BottomRight", StartColumn = 1, StartRow = 1, ColumnSpan = 3, RowSpan = 2, CellNumbers = [6, 7, 8, 10, 11, 12] },
            new SlotDefinition { Id = "C", Name = "TopRight", StartColumn = 1, StartRow = 0, ColumnSpan = 3, RowSpan = 2, CellNumbers = [2, 3, 4, 6, 7, 8] },
            new SlotDefinition { Id = "D", Name = "BottomLeft", StartColumn = 0, StartRow = 1, ColumnSpan = 3, RowSpan = 2, CellNumbers = [5, 6, 7, 9, 10, 11] }
        };
    }

    public IReadOnlyList<WindowPlacement> CalculatePlacements(
        IReadOnlyList<ManagedWindow> windows,
        Rect workArea,
        AppSettings settings)
    {
        var result = new List<WindowPlacement>();
        if (windows.Count == 0) return result;

        var priorityWindows = windows.OrderByDescending(w => w.IsPinned)
                                     .ThenByDescending(w => w.IsForeground)
                                     .ThenByDescending(w => w.LastFocusedAt ?? w.FirstSeenAt)
                                     .Take(4)
                                     .ToList();

        var grid = CreateDefaultGrid(workArea);
        var slots = CreateDefaultSlots(grid);

        if (priorityWindows.Count == 1)
        {
            Rect targetRect;
            if (settings.SingleWindowMode == "TopLeftSlot")
            {
                targetRect = CalculateSlotRect(slots.First(s => s.Id == "A"), grid);
            }
            else
            {
                targetRect = workArea;
            }
            
            result.Add(new WindowPlacement
            {
                Hwnd = priorityWindows[0].Hwnd,
                SlotId = settings.SingleWindowMode == "TopLeftSlot" ? "A" : "FocusLarge",
                TargetRect = targetRect,
                Activate = false
            });
            return result;
        }

        string[] slotIds = { "A", "B", "C", "D" };

        for (int i = 0; i < priorityWindows.Count; i++)
        {
            var win = priorityWindows[i];
            var targetSlot = slots.First(s => s.Id == slotIds[i % slotIds.Length]);
            var targetRect = CalculateSlotRect(targetSlot, grid);

            result.Add(new WindowPlacement
            {
                Hwnd = win.Hwnd,
                SlotId = targetSlot.Id,
                TargetRect = targetRect,
                Activate = false
            });
        }

        return result;
    }

    public Rect CalculateSlotRect(SlotDefinition slot, GridSpec grid)
    {
        int x = grid.WorkArea.Left + (slot.StartColumn * grid.CellWidth);
        int y = grid.WorkArea.Top + (slot.StartRow * grid.CellHeight);
        int width = slot.ColumnSpan * grid.CellWidth;
        int height = slot.RowSpan * grid.CellHeight;

        bool touchesRight = (slot.StartColumn + slot.ColumnSpan) == grid.Columns;
        bool touchesBottom = (slot.StartRow + slot.RowSpan) == grid.Rows;

        if (touchesRight)
        {
            width = grid.WorkArea.Right - x;
        }

        if (touchesBottom)
        {
            height = grid.WorkArea.Bottom - y;
        }

        return new Rect(x, y, width, height);
    }
}
