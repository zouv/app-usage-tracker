namespace AppUsageTracker.ViewModels;

/// <summary>
/// 柱状图数据契约与视窗换算。与 WPF 无关的纯逻辑，便于单元测试覆盖
/// 缩放、平移和槽位合并的边界。
/// </summary>
public static class ChartLayout
{
    /// <summary>最大放大倍数，避免无限放大后单柱撑满整个绘图区。</summary>
    public const double MaxZoom = 40d;

    /// <summary>单个槽位低于该像素宽度时按整数倍合并，避免柱子糊成一片。</summary>
    public const double MinSlotWidth = 6d;

    /// <summary>单柱最大像素宽度；超出部分留作槽位之间的空气。</summary>
    public const double MaxBarWidth = 26d;

    /// <summary>把缩放倍数夹到合法区间。</summary>
    public static double ClampZoom(double zoom) => Math.Clamp(zoom, 1d, MaxZoom);

    /// <summary>
    /// 把平移偏移夹到合法区间。偏移以“视窗左边界占总宽度的比例”表示，
    /// 因此上界是 1 - 1/zoom，保证视窗右边界不越过数据末尾。
    /// </summary>
    public static double ClampOffset(double offset, double zoom)
    {
        var max = Math.Max(0d, 1d - 1d / ClampZoom(zoom));
        return Math.Clamp(offset, 0d, max);
    }

    /// <summary>
    /// 以鼠标位置为锚点缩放：锚点下的数据点在缩放前后停留在同一像素位置。
    /// <paramref name="anchorRatio"/> 是鼠标在绘图区内的横向比例（0~1）。
    /// </summary>
    public static (double Zoom, double Offset) ZoomAt(
        double zoom,
        double offset,
        double anchorRatio,
        double factor)
    {
        var oldZoom = ClampZoom(zoom);
        var newZoom = ClampZoom(oldZoom * factor);
        var anchor = Math.Clamp(anchorRatio, 0d, 1d);

        // 锚点对应的数据比例在缩放前后保持不变。
        var dataRatio = offset + anchor / oldZoom;
        var newOffset = dataRatio - anchor / newZoom;
        return (newZoom, ClampOffset(newOffset, newZoom));
    }

    /// <summary>
    /// 计算当前视窗覆盖的槽位区间（左闭右开），并额外向两侧各留一个槽位，
    /// 使半露在边界上的柱子仍能绘制。
    /// </summary>
    public static (int Start, int End) VisibleRange(int slotCount, double zoom, double offset)
    {
        if (slotCount <= 0)
        {
            return (0, 0);
        }

        var clampedZoom = ClampZoom(zoom);
        var clampedOffset = ClampOffset(offset, clampedZoom);
        var start = (int)Math.Floor(clampedOffset * slotCount) - 1;
        var end = (int)Math.Ceiling((clampedOffset + 1d / clampedZoom) * slotCount) + 1;
        return (Math.Max(0, start), Math.Min(slotCount, Math.Max(0, end)));
    }

    /// <summary>
    /// 槽位过密时按整数倍合并。<paramref name="steps"/> 给出业务上有意义的候选倍数，
    /// 取其中第一个能让柱宽达标的值；都不够时取最大的那个。
    /// </summary>
    public static int ComputeGrouping(
        int slotCount,
        double zoom,
        double plotWidth,
        IReadOnlyList<int>? steps = null)
    {
        if (slotCount <= 0 || plotWidth <= 0)
        {
            return 1;
        }

        var slotWidth = plotWidth * ClampZoom(zoom) / slotCount;
        if (slotWidth >= MinSlotWidth)
        {
            return 1;
        }

        var needed = MinSlotWidth / Math.Max(slotWidth, 0.0001);
        if (steps is null || steps.Count == 0)
        {
            return Math.Max(1, (int)Math.Ceiling(needed));
        }

        foreach (var step in steps)
        {
            if (step >= needed)
            {
                return Math.Max(1, step);
            }
        }

        return Math.Max(1, steps[^1]);
    }

    /// <summary>按可读性挑选刻度步长，使刻度数量落在 4~12 之间。</summary>
    public static int ComputeTickStep(int visibleSlots)
    {
        if (visibleSlots <= 0)
        {
            return 1;
        }

        var target = Math.Max(1, visibleSlots / 8);
        int[] candidates = [1, 2, 3, 5, 6, 10, 12, 15, 20, 30, 60, 120, 180, 360, 720];
        foreach (var candidate in candidates)
        {
            if (candidate >= target)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }
}
