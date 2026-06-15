using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class LayoutEngine
{
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
