using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Tests;

public class ChartLayoutTests
{
    [Fact]
    public void ClampZoom_KeepsValueInsideRange()
    {
        Assert.Equal(1d, ChartLayout.ClampZoom(0.2d));
        Assert.Equal(ChartLayout.MaxZoom, ChartLayout.ClampZoom(ChartLayout.MaxZoom * 3));
        Assert.Equal(4d, ChartLayout.ClampZoom(4d));
    }

    [Fact]
    public void ClampOffset_StopsViewportAtDataEnd()
    {
        // 放大 4 倍时视窗占 1/4，左边界最多推到 0.75。
        Assert.Equal(0.75d, ChartLayout.ClampOffset(0.9d, 4d), 6);
        Assert.Equal(0d, ChartLayout.ClampOffset(-0.3d, 4d));
        Assert.Equal(0d, ChartLayout.ClampOffset(0.5d, 1d));
    }

    [Fact]
    public void ZoomAt_KeepsAnchorPointUnderCursor()
    {
        var (zoom, offset) = ChartLayout.ZoomAt(1d, 0d, 0.5d, 2d);

        Assert.Equal(2d, zoom, 6);
        // 缩放前光标下是数据的 50%，缩放后仍应落在光标处。
        Assert.Equal(0.5d, offset + 0.5d / zoom, 6);
    }

    [Fact]
    public void ZoomAt_ClampsBackToFullRangeWhenZoomingOut()
    {
        var (zoom, offset) = ChartLayout.ZoomAt(2d, 0.5d, 1d, 0.25d);

        Assert.Equal(1d, zoom, 6);
        Assert.Equal(0d, offset, 6);
    }

    [Fact]
    public void VisibleRange_CoversViewportWithOneSlotMargin()
    {
        var (start, end) = ChartLayout.VisibleRange(100, 4d, 0.5d);

        // 视窗覆盖 50~75，两侧各留一个槽位。
        Assert.Equal(49, start);
        Assert.Equal(76, end);
    }

    [Fact]
    public void VisibleRange_StaysInsideBoundsAtEdges()
    {
        var (start, end) = ChartLayout.VisibleRange(10, 1d, 0d);

        Assert.Equal(0, start);
        Assert.Equal(10, end);
    }

    [Fact]
    public void VisibleRange_ReturnsEmptyForNoSlots()
    {
        var (start, end) = ChartLayout.VisibleRange(0, 2d, 0d);

        Assert.Equal(0, start);
        Assert.Equal(0, end);
    }

    [Fact]
    public void ComputeGrouping_ReturnsOneWhenSlotsAreWideEnough()
    {
        Assert.Equal(1, ChartLayout.ComputeGrouping(50, 1d, 800d, [1, 2, 5]));
    }

    [Fact]
    public void ComputeGrouping_SnapsToProvidedSteps()
    {
        // 600 个槽位挤在 800 像素里，单槽约 1.33 像素，需要合并 5 个才够 6 像素。
        Assert.Equal(5, ChartLayout.ComputeGrouping(600, 1d, 800d, [1, 2, 5, 10, 15]));
    }

    [Fact]
    public void ComputeGrouping_FallsBackToLargestStepWhenNoneFits()
    {
        Assert.Equal(2, ChartLayout.ComputeGrouping(10000, 1d, 800d, [1, 2]));
    }

    [Fact]
    public void ComputeGrouping_ShrinksAsZoomIncreases()
    {
        int[] steps = [1, 2, 5, 10, 15, 30, 60];
        var atRest = ChartLayout.ComputeGrouping(1440, 1d, 900d, steps);
        var zoomedIn = ChartLayout.ComputeGrouping(1440, 10d, 900d, steps);

        Assert.True(zoomedIn < atRest);
        Assert.Equal(1, ChartLayout.ComputeGrouping(1440, 40d, 900d, steps));
    }

    [Fact]
    public void ComputeTickStep_KeepsTickCountReadable()
    {
        Assert.Equal(1, ChartLayout.ComputeTickStep(6));
        Assert.True(ChartLayout.ComputeTickStep(1440) >= 180);
        Assert.Equal(1, ChartLayout.ComputeTickStep(0));
    }
}
