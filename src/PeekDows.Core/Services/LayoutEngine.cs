using System;
using System.Collections.Generic;
using System.Linq;
using PeekDows.Core.Models;

namespace PeekDows.Core.Services;

public class LayoutEngine
{
    private const double ClassicPeekGridWidthRatio = 0.90;
    private const double ClassicPeekGridHeightRatio = 0.90;

    public IReadOnlyList<WindowPlacement> CalculateClassicPeekGridPlacements(
        IReadOnlyList<ManagedWindow> windows,
        Rect workArea,
        AppSettings settings)
    {
        var result = new List<WindowPlacement>();
        if (windows.Count == 0) return result;

        var priorityWindows = PrioritizeForClassicPeekGrid(windows);

        var slotRects = CalculateClassicPeekGridSlotRects(workArea);

        if (priorityWindows.Count == 1)
        {
            Rect targetRect;
            string slotId;

            if (settings.SingleWindowMode != "TopLeftSlot")
            {
                targetRect = workArea;
                slotId = "FocusLarge";
            }
            else
            {
                targetRect = slotRects["A"];
                slotId = "A";
            }

            result.Add(new WindowPlacement
            {
                Hwnd = priorityWindows[0].Hwnd,
                SlotId = slotId,
                TargetRect = targetRect,
                BringToFront = false
            });

            return result;
        }

        string[] slotOrder = { "A", "B", "C", "D" };

        for (int i = 0; i < priorityWindows.Count; i++)
        {
            var slotId = slotOrder[i % slotOrder.Length];
            result.Add(new WindowPlacement
            {
                Hwnd = priorityWindows[i].Hwnd,
                SlotId = slotId,
                TargetRect = slotRects[slotId],
                BringToFront = false
            });
        }

        return result;
    }

    /// <summary>
    /// Computes the four ClassicPeekGrid (90% overlap) slot rects for a monitor work area,
    /// using exactly the same math as <see cref="CalculateClassicPeekGridPlacements"/> so a
    /// window physically sitting in one of these rects can be recognised as belonging to
    /// that slot without being re-arranged.
    /// </summary>
    /// <remarks>
    /// Slot id mapping: A = TopLeft, B = BottomRight, C = TopRight, D = BottomLeft.
    /// </remarks>
    public IReadOnlyDictionary<string, Rect> CalculateClassicPeekGridSlotRects(Rect workArea)
    {
        int spanW = (int)Math.Round(workArea.Width * ClassicPeekGridWidthRatio);
        int spanH = (int)Math.Round(workArea.Height * ClassicPeekGridHeightRatio);

        spanW = Math.Clamp(spanW, 1, workArea.Width);
        spanH = Math.Clamp(spanH, 1, workArea.Height);

        int leftX = workArea.Left;
        int rightX = workArea.Right - spanW;
        int topY = workArea.Top;
        int bottomY = workArea.Bottom - spanH;

        return new Dictionary<string, Rect>
        {
            ["A"] = new Rect(leftX, topY, spanW, spanH),
            ["B"] = new Rect(rightX, bottomY, spanW, spanH),
            ["C"] = new Rect(rightX, topY, spanW, spanH),
            ["D"] = new Rect(leftX, bottomY, spanW, spanH)
        };
    }

    private IReadOnlyList<ManagedWindow> PrioritizeForClassicPeekGrid(IReadOnlyList<ManagedWindow> windows)
    {
        return windows
            .OrderByDescending(w => w.IsPinned)
            .ThenByDescending(w => w.IsForeground)
            .ThenByDescending(w => w.LastFocusedAt ?? w.FirstSeenAt)
            .Take(4)
            .ToList();
    }

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

    public IReadOnlyList<WindowPlacement> CalculateEnhancedPeekGridPlacements(
        IReadOnlyList<ManagedWindow> windows,
        Rect workArea,
        AppSettings settings)
    {
        var result = new List<WindowPlacement>();
        if (windows.Count == 0) return result;

        var priorityWindows = PrioritizeForEnhancedPeekGrid(windows);

        int focusMarginH = (int)(workArea.Width * 0.06);
        int focusMarginV = (int)(workArea.Height * 0.06);
        focusMarginH = Math.Clamp(focusMarginH, 40, 100);
        focusMarginV = Math.Clamp(focusMarginV, 30, 70);

        var focusRect = new Rect(
            workArea.Left + focusMarginH,
            workArea.Top + focusMarginV,
            workArea.Width - (focusMarginH * 2),
            workArea.Height - (focusMarginV * 2)
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
        var secondaryWindows = priorityWindows.Skip(1).Take(3).ToList();

        int secondaryW = (int)(workArea.Width * 0.67);
        int secondaryH = (int)(workArea.Height * 0.64);

        int secondaryInsetH = (int)(workArea.Width * 0.08);
        int secondaryInsetV = (int)(workArea.Height * 0.08);

        var bottomRightRect = new Rect(
            workArea.Left + secondaryInsetH,
            workArea.Top + workArea.Height - secondaryH - secondaryInsetV,
            secondaryW,
            secondaryH
        );

        var topRightRect = new Rect(
            workArea.Left + secondaryInsetH,
            workArea.Top + secondaryInsetV,
            secondaryW,
            secondaryH
        );

        var bottomLeftRect = new Rect(
            workArea.Left + workArea.Width - secondaryW - secondaryInsetH,
            workArea.Top + workArea.Height - secondaryH - secondaryInsetV,
            secondaryW,
            secondaryH
        );

        Rect[] secondaryRects = [bottomRightRect, topRightRect, bottomLeftRect];
        string[] secondarySlotIds = ["BottomRight", "TopRight", "BottomLeft"];

        for (int i = 0; i < secondaryWindows.Count; i++)
        {
            result.Add(new WindowPlacement
            {
                Hwnd = secondaryWindows[i].Hwnd,
                SlotId = secondarySlotIds[i],
                TargetRect = secondaryRects[i],
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

    private IReadOnlyList<ManagedWindow> PrioritizeForEnhancedPeekGrid(IReadOnlyList<ManagedWindow> windows)
    {
        return windows
            .OrderByDescending(w => w.IsPinned)
            .ThenByDescending(w => w.IsForeground)
            .ThenByDescending(w => w.LastFocusedAt ?? DateTime.MinValue)
            .ThenByDescending(w => w.CurrentRect.Width * w.CurrentRect.Height)
            .Take(4)
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
