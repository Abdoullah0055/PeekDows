using PeekDows.Core.Models;
using PeekDows.Core.Services;

namespace PeekDows.Tests;

public class ArrowGeometryTests
{
    private const float Center = 100f;
    private const float Inner = 16f;
    private const float Reach = 80f;
    private const float Bonus = 8f;
    private const float HeadLen = 11f;
    private const float HeadHalf = 5f;
    private const float Scale = 1f;

    private static readonly DirectionalFocusSlot[] AllEight =
    [
        DirectionalFocusSlot.TopLeft,
        DirectionalFocusSlot.TopCenter,
        DirectionalFocusSlot.TopRight,
        DirectionalFocusSlot.MiddleRight,
        DirectionalFocusSlot.BottomRight,
        DirectionalFocusSlot.BottomCenter,
        DirectionalFocusSlot.BottomLeft,
        DirectionalFocusSlot.MiddleLeft,
    ];

    private static IReadOnlyList<((float X, float Y) P1, (float X, float Y) Tip, (float X, float Y)[] Head)> Arrows(
        IEnumerable<DirectionalFocusSlot> populated, DirectionalFocusSlot? active = null)
        => ArrowGeometry.ComputeArrows(populated, active, Center, Inner, Reach, Bonus, HeadLen, HeadHalf, Scale);

    private static float Dist((float X, float Y) p)
        => (float)Math.Sqrt((p.X - Center) * (p.X - Center) + (p.Y - Center) * (p.Y - Center));

    [Fact]
    public void TwoDiagonalSlots_ReturnsTopLeftAndBottomRight()
    {
        var arrows = Arrows([DirectionalFocusSlot.TopLeft, DirectionalFocusSlot.BottomRight]);

        Assert.Equal(2, arrows.Count);
        // Canonical order: TopLeft first, BottomRight second.
        Assert.True(arrows[0].Tip.X < Center && arrows[0].Tip.Y < Center);
        Assert.True(arrows[1].Tip.X > Center && arrows[1].Tip.Y > Center);
    }

    [Fact]
    public void InputOrder_DoesNotAffectCanonicalOutputOrder()
    {
        var arrows = Arrows([DirectionalFocusSlot.BottomRight, DirectionalFocusSlot.TopLeft]);

        Assert.Equal(2, arrows.Count);
        Assert.True(arrows[0].Tip.X < Center && arrows[0].Tip.Y < Center);
        Assert.True(arrows[1].Tip.X > Center && arrows[1].Tip.Y > Center);
    }

    [Fact]
    public void FourCorners_ReturnsFourDiagonals()
    {
        var arrows = Arrows([
            DirectionalFocusSlot.TopLeft, DirectionalFocusSlot.TopRight,
            DirectionalFocusSlot.BottomLeft, DirectionalFocusSlot.BottomRight]);

        Assert.Equal(4, arrows.Count);
        Assert.True(arrows[0].Tip.X < Center && arrows[0].Tip.Y < Center); // TopLeft
        Assert.True(arrows[1].Tip.X > Center && arrows[1].Tip.Y < Center); // TopRight
        Assert.True(arrows[2].Tip.X > Center && arrows[2].Tip.Y > Center); // BottomRight
        Assert.True(arrows[3].Tip.X < Center && arrows[3].Tip.Y > Center); // BottomLeft
    }

    [Fact]
    public void AllEight_ReturnsEight()
    {
        var arrows = Arrows(AllEight);

        Assert.Equal(8, arrows.Count);
    }

    [Fact]
    public void ActiveSlot_IsLongerByBonus()
    {
        var arrows = Arrows(AllEight, DirectionalFocusSlot.TopLeft);

        // Idle tip sits at reach + 2px overshoot; active adds the bonus.
        Assert.Equal(Reach + 2f, Dist(arrows[1].Tip), 2);
        Assert.Equal(Reach + Bonus + 2f, Dist(arrows[0].Tip), 2);
        // Shaft start is on the inner radius for both.
        Assert.Equal(Inner, Dist(arrows[0].P1), 2);
        Assert.Equal(Inner, Dist(arrows[1].P1), 2);
    }

    [Fact]
    public void Head_IsTipFirstTriangleOfHeadLen()
    {
        var arrows = Arrows([DirectionalFocusSlot.TopCenter]);

        var head = arrows[0].Head;
        Assert.Equal(3, head.Length);
        Assert.Equal(arrows[0].Tip, head[0]);
        float baseMidX = (head[1].X + head[2].X) / 2f;
        float baseMidY = (head[1].Y + head[2].Y) / 2f;
        float len = (float)Math.Sqrt(
            (arrows[0].Tip.X - baseMidX) * (arrows[0].Tip.X - baseMidX) +
            (arrows[0].Tip.Y - baseMidY) * (arrows[0].Tip.Y - baseMidY));
        Assert.Equal(HeadLen, len, 2);
    }

    [Fact]
    public void Empty_ReturnsZero()
    {
        Assert.Empty(Arrows([]));
    }

    [Fact]
    public void Null_ReturnsZero()
    {
        Assert.Empty(ArrowGeometry.ComputeArrows(
            null!, null, Center, Inner, Reach, Bonus, HeadLen, HeadHalf, Scale));
    }

    [Theory]
    [InlineData(DirectionalFocusSlot.MiddleRight, 1f, 0f)]
    [InlineData(DirectionalFocusSlot.BottomCenter, 0f, 1f)]
    [InlineData(DirectionalFocusSlot.MiddleLeft, -1f, 0f)]
    [InlineData(DirectionalFocusSlot.TopCenter, 0f, -1f)]
    public void Directions_Cardinals_AreUnitAxis(DirectionalFocusSlot slot, float ex, float ey)
    {
        var (dx, dy) = ArrowGeometry.Direction(slot);

        Assert.Equal(ex, dx, 5);
        Assert.Equal(ey, dy, 5);
    }

    [Theory]
    [InlineData(DirectionalFocusSlot.TopLeft, -0.7071f, -0.7071f)]
    [InlineData(DirectionalFocusSlot.TopRight, 0.7071f, -0.7071f)]
    [InlineData(DirectionalFocusSlot.BottomRight, 0.7071f, 0.7071f)]
    [InlineData(DirectionalFocusSlot.BottomLeft, -0.7071f, 0.7071f)]
    public void Directions_Diagonals_AreUnitDiagonal(DirectionalFocusSlot slot, float ex, float ey)
    {
        var (dx, dy) = ArrowGeometry.Direction(slot);

        Assert.Equal(ex, dx, 4);
        Assert.Equal(ey, dy, 4);
    }

    [Fact]
    public void Directions_AllAreUnitLength()
    {
        foreach (var slot in AllEight)
        {
            var (dx, dy) = ArrowGeometry.Direction(slot);
            Assert.Equal(1f, (float)Math.Sqrt(dx * dx + dy * dy), 4);
        }
    }
}
