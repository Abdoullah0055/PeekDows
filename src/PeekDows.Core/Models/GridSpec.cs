namespace PeekDows.Core.Models;

public sealed class GridSpec
{
    public Rect WorkArea { get; init; }
    public int Columns { get; init; } = 4;
    public int Rows { get; init; } = 3;
    public int CellWidth { get; init; }
    public int CellHeight { get; init; }
}

public sealed class SlotDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int StartColumn { get; init; }
    public int StartRow { get; init; }
    public int ColumnSpan { get; init; }
    public int RowSpan { get; init; }
    public int[] CellNumbers { get; init; } = [];
}

public sealed class WindowPlacement
{
    public System.IntPtr Hwnd { get; init; }
    public string SlotId { get; init; } = "";
    public Rect TargetRect { get; init; }
    public bool Activate { get; init; }
    public bool PreserveZOrder { get; init; }
}
