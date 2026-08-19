namespace AppUsageTracker.ViewModels;

/// <summary>
/// 时间轴窗口计算。与 WPF 无关的纯逻辑，负责按整点对齐出一个覆盖当天全部
/// 活动的时间窗，供 <c>ChartBuilder</c> 分箱使用。
/// </summary>
public static class TimelineLayout
{
    /// <summary>时间轴最短跨度，避免只有一两段时被过度拉伸。</summary>
    public const int MinimumAxisHours = 4;

    /// <summary>
    /// 按整点对齐计算时间窗，保证覆盖 <paramref name="earliest"/> 到
    /// <paramref name="latest"/> 的全部活动，且不少于 <see cref="MinimumAxisHours"/> 小时。
    /// 窗口始终落在 <paramref name="dayStart"/> 当天之内。
    /// </summary>
    public static (DateTime Start, DateTime End) ComputeWindow(
        DateTime earliest,
        DateTime latest,
        DateTime dayStart)
    {
        var dayEnd = dayStart.AddDays(1);
        var start = dayStart.AddHours(Math.Floor((earliest - dayStart).TotalHours));
        var end = dayStart.AddHours(Math.Ceiling((latest - dayStart).TotalHours));

        if (start < dayStart)
        {
            start = dayStart;
        }

        if (end > dayEnd)
        {
            end = dayEnd;
        }

        if ((end - start).TotalHours >= MinimumAxisHours)
        {
            return (start, end);
        }

        // 优先向后扩展；触到当天末尾时改为向前扩展。
        end = start.AddHours(MinimumAxisHours);
        if (end <= dayEnd)
        {
            return (start, end);
        }

        end = dayEnd;
        start = end.AddHours(-MinimumAxisHours);
        return (start < dayStart ? dayStart : start, end);
    }
}
