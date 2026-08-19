using System.Windows.Media;

namespace AppUsageTracker.ViewModels;

/// <summary>柱状图里的一个系列，对应一个软件（或“未匹配”）。</summary>
public sealed class ChartSeries
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>系列色。已按当前主题解析过，不是持久化的槽位色。</summary>
    public required Color Color { get; init; }

    /// <summary>该系列在整个图表中的总秒数，用于图例排序和图例上的时长。</summary>
    public long TotalSeconds { get; set; }

    /// <summary>供图例色块直接绑定的画刷。</summary>
    public Brush Brush => _brush ??= CreateBrush();

    private Brush? _brush;

    public string Duration => DurationFormatter.Format(TotalSeconds);

    private Brush CreateBrush()
    {
        var brush = new SolidColorBrush(Color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>柱状图里的一个槽位：时间线的一个时间片，或统计页的一天。</summary>
public sealed class ChartSlot
{
    /// <summary>横轴上的短标签，例如 <c>09:00</c> 或 <c>3日</c>。</summary>
    public required string Label { get; init; }

    /// <summary>槽位起点的完整说明，用于悬浮提示。</summary>
    public required string RangeStart { get; init; }

    /// <summary>槽位终点的完整说明；与起点相同表示是一个不可再分的点。</summary>
    public required string RangeEnd { get; init; }

    /// <summary>该槽位内各系列的秒数，键为 <see cref="ChartSeries.Key"/>。</summary>
    public Dictionary<string, long> Values { get; init; } = [];

    public long TotalSeconds => Values.Values.Sum();
}

/// <summary>柱状图的一份完整数据，由 ViewModel 组装后整体赋给控件。</summary>
public sealed class ChartData
{
    public static readonly ChartData Empty = new();

    public IReadOnlyList<ChartSeries> Series { get; init; } = [];

    public IReadOnlyList<ChartSlot> Slots { get; init; } = [];

    /// <summary>纵轴单位：分钟或小时。</summary>
    public string ValueUnit { get; init; } = "分钟";

    /// <summary>
    /// 槽位过密时允许的合并倍数。取业务上有意义的整数（分钟刻度、周、月），
    /// 避免合并出“13 分钟”这种读不出来的柱宽。
    /// </summary>
    public IReadOnlyList<int> GroupingSteps { get; init; } = [1];

    /// <summary>是否有可绘制的数据；界面据此在图表和“暂无记录”之间切换。</summary>
    public bool HasData => Slots.Count > 0 && Series.Count > 0;

    /// <summary>把秒换算成纵轴单位上的数值。</summary>
    public double ToAxisValue(long seconds) =>
        ValueUnit == "小时" ? seconds / 3600d : seconds / 60d;
}
