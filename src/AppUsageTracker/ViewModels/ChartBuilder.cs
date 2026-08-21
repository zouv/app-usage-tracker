using System.Windows.Media;
using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.ViewModels;

/// <summary>
/// 柱状图数据组装。把活动会话和统计结果换算成 <see cref="ChartData"/>，
/// 与控件解耦，便于单元测试覆盖分箱和跨槽位摊分的边界。
/// </summary>
public static class ChartBuilder
{
    /// <summary>时间线的基础分箱粒度：1 分钟。更粗的粒度由控件按缩放倍数合并得到。</summary>
    public const int TimelineBinMinutes = 1;

    /// <summary>时间线允许的合并倍数，取整分钟的常用刻度，避免出现 13 分钟这种柱宽。</summary>
    public static readonly int[] TimelineGroupingSteps =
        [1, 2, 5, 10, 15, 20, 30, 60, 120, 180, 360];

    /// <summary>每日柱状图允许的合并倍数：日、周、旬、月。</summary>
    public static readonly int[] DailyGroupingSteps = [1, 2, 7, 14, 30, 90];

    /// <summary>
    /// 组装今日活动时间线。窗口按整点对齐覆盖全部活动，
    /// 每个会话按与分箱的重叠比例把有效时长摊分到各个分箱里。
    /// </summary>
    public static ChartData BuildTimeline(
        IReadOnlyCollection<ActivitySession> sessions,
        IReadOnlyCollection<TrackedApp> apps,
        DateTime day)
    {
        var appMap = apps.ToDictionary(app => app.Id);
        // 与统计口径保持一致：只画有效前台会话，锁屏和空闲段不占时间线。
        var todaySessions = sessions
            .Where(item =>
                item.EndedAtUtc is not null &&
                item.DurationSeconds > 0 &&
                item.State == ActivityState.Active &&
                item.StartedAtUtc.ToLocalTime().Date == day.Date)
            .OrderBy(item => item.StartedAtUtc)
            .ToList();

        if (todaySessions.Count == 0)
        {
            return ChartData.Empty;
        }

        var earliest = todaySessions.Min(item => item.StartedAtUtc.ToLocalTime());
        var latest = todaySessions.Max(item => item.EndedAtUtc!.Value.ToLocalTime());
        var (windowStart, windowEnd) = TimelineLayout.ComputeWindow(earliest, latest, day.Date);

        var binSpan = TimeSpan.FromMinutes(TimelineBinMinutes);
        var binCount = (int)Math.Ceiling((windowEnd - windowStart) / binSpan);
        if (binCount <= 0)
        {
            return ChartData.Empty;
        }

        var slots = new ChartSlot[binCount];
        for (var index = 0; index < binCount; index++)
        {
            var binStart = windowStart.Add(binSpan * index);
            var binEnd = binStart.Add(binSpan);
            slots[index] = new ChartSlot
            {
                Label = binStart.ToString("HH:mm"),
                RangeStart = binStart.ToString("HH:mm"),
                RangeEnd = binEnd.ToString("HH:mm"),
            };
        }

        var totals = new Dictionary<string, long>();
        var names = new Dictionary<string, string>();
        var colors = new Dictionary<string, string>();

        foreach (var session in todaySessions)
        {
            var start = session.StartedAtUtc.ToLocalTime();
            var end = session.EndedAtUtc!.Value.ToLocalTime();
            if (end <= windowStart || start >= windowEnd)
            {
                continue;
            }

            var app = session.ApplicationId is { } id ? appMap.GetValueOrDefault(id) : null;
            var key = session.ApplicationId?.ToString("N") ?? "unmatched";
            names[key] = app?.Name ?? LocalizationService.T("Loc.Chart.Unmatched");
            colors[key] = app?.ColorHex ?? AppColorPalette.NeutralHex;

            // 会话的有效时长可能小于墙上时间，按重叠比例等比摊分到各分箱。
            var wallSeconds = Math.Max(1d, (end - start).TotalSeconds);
            var firstBin = Math.Max(0, (int)((start - windowStart) / binSpan));
            var lastBin = Math.Min(binCount - 1, (int)((end - windowStart) / binSpan));

            for (var index = firstBin; index <= lastBin; index++)
            {
                var binStart = windowStart.Add(binSpan * index);
                var binEnd = binStart.Add(binSpan);
                var overlapStart = start > binStart ? start : binStart;
                var overlapEnd = end < binEnd ? end : binEnd;
                var overlap = (overlapEnd - overlapStart).TotalSeconds;
                if (overlap <= 0)
                {
                    continue;
                }

                var seconds = (long)Math.Round(session.DurationSeconds * (overlap / wallSeconds));
                if (seconds <= 0)
                {
                    continue;
                }

                var values = slots[index].Values;
                values[key] = values.GetValueOrDefault(key) + seconds;
                totals[key] = totals.GetValueOrDefault(key) + seconds;
            }
        }

        return new ChartData
        {
            Series = BuildSeries(totals, names, colors),
            Slots = slots,
            Unit = ChartValueUnit.Minutes,
            GroupingSteps = TimelineGroupingSteps,
        };
    }

    /// <summary>
    /// 组装每日使用时长。<paramref name="points"/> 只包含有记录的日期，
    /// 这里按 <paramref name="rangeStart"/> 到 <paramref name="rangeEnd"/> 补齐空白日，
    /// 使横轴的日期间距与真实日历一致。
    /// </summary>
    public static ChartData BuildDaily(
        IReadOnlyCollection<DailyUsagePoint> points,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        if (points.Count == 0)
        {
            return ChartData.Empty;
        }

        var lookup = points.ToDictionary(item => item.Date);
        var first = points.Min(item => item.Date);
        var last = points.Max(item => item.Date);

        // 周、月等固定周期用请求区间；“全部”这类开放区间退回到实际数据范围。
        var start = rangeStart == DateTime.MinValue ? first : DateOnly.FromDateTime(rangeStart);
        var end = rangeEnd == DateTime.MaxValue
            ? last
            : DateOnly.FromDateTime(rangeEnd.AddDays(-1));
        if (start > first)
        {
            start = first;
        }

        if (end < last)
        {
            end = last;
        }

        var dayCount = end.DayNumber - start.DayNumber + 1;
        if (dayCount <= 0)
        {
            return ChartData.Empty;
        }

        var slots = new ChartSlot[dayCount];
        var totals = new Dictionary<string, long>();
        var names = new Dictionary<string, string>();
        var colors = new Dictionary<string, string>();

        for (var index = 0; index < dayCount; index++)
        {
            var date = start.AddDays(index);
            var label = date.ToDateTime(TimeOnly.MinValue).ToString("M/d");
            var slot = new ChartSlot
            {
                Label = label,
                RangeStart = LocalizationService.FullDateWithWeekday(date.ToDateTime(TimeOnly.MinValue)),
                RangeEnd = LocalizationService.FullDateWithWeekday(date.ToDateTime(TimeOnly.MinValue)),
            };
            slots[index] = slot;

            if (!lookup.TryGetValue(date, out var point))
            {
                continue;
            }

            foreach (var app in point.Apps)
            {
                var key = app.ApplicationId.ToString("N");
                names[key] = app.Name;
                colors[key] = app.ColorHex;
                slot.Values[key] = slot.Values.GetValueOrDefault(key) + app.Seconds;
                totals[key] = totals.GetValueOrDefault(key) + app.Seconds;
            }
        }

        return new ChartData
        {
            Series = BuildSeries(totals, names, colors),
            Slots = slots,
            Unit = ChartValueUnit.Hours,
            GroupingSteps = DailyGroupingSteps,
        };
    }

    /// <summary>
    /// 按软件聚合的柱状图：每个软件一根柱，按总时长降序排列。
    /// 用于「日」和「全部」这类没有时间轴、只关心各软件时长的视图。
    /// </summary>
    public static ChartData BuildByApp(IReadOnlyCollection<DailyUsagePoint> points)
    {
        if (points.Count == 0)
        {
            return ChartData.Empty;
        }

        var totals = new Dictionary<string, long>();
        var names = new Dictionary<string, string>();
        var colors = new Dictionary<string, string>();
        foreach (var point in points)
        {
            foreach (var app in point.Apps)
            {
                var key = app.ApplicationId.ToString("N");
                names[key] = app.Name;
                colors[key] = app.ColorHex;
                totals[key] = totals.GetValueOrDefault(key) + app.Seconds;
            }
        }

        // 柱序与系列序都按总时长降序，保证每根柱的颜色与图例一一对应。
        var ordered = totals
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => names.GetValueOrDefault(pair.Key), StringComparer.CurrentCulture)
            .ToList();

        var slots = ordered.Select(pair => new ChartSlot
        {
            Label = names.GetValueOrDefault(pair.Key) ?? LocalizationService.T("Loc.Chart.Unmatched"),
            RangeStart = names.GetValueOrDefault(pair.Key) ?? LocalizationService.T("Loc.Chart.Unmatched"),
            RangeEnd = names.GetValueOrDefault(pair.Key) ?? LocalizationService.T("Loc.Chart.Unmatched"),
            Values = new Dictionary<string, long> { [pair.Key] = pair.Value },
        }).ToList();

        return new ChartData
        {
            Series = BuildSeries(totals, names, colors),
            Slots = slots,
            Unit = ChartValueUnit.Hours,
            // 按软件绘制时每根柱就是一个软件，绝不允许合并到同一个槽位里。
            GroupingSteps = [1],
        };
    }

    /// <summary>
    /// 按周聚合的柱状图：每周一根堆叠柱，横轴为周一起始日期。
    /// 与 <see cref="BuildDaily"/> 一样补齐空白周，使间距贴合真实日历。
    /// </summary>
    public static ChartData BuildWeekly(
        IReadOnlyCollection<DailyUsagePoint> points,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        if (points.Count == 0)
        {
            return ChartData.Empty;
        }

        var lookup = points.ToDictionary(item => item.Date);
        var rangeStartDate = DateOnly.FromDateTime(rangeStart.Date);
        var rangeEndDate = DateOnly.FromDateTime(rangeEnd.Date);
        var weekStart = StartOfWeek(rangeStartDate);
        if (weekStart >= rangeEndDate)
        {
            return ChartData.Empty;
        }

        var slots = new List<ChartSlot>();
        var totals = new Dictionary<string, long>();
        var names = new Dictionary<string, string>();
        var colors = new Dictionary<string, string>();

        for (var current = weekStart; current < rangeEndDate; current = current.AddDays(7))
        {
            var weekEnd = current.AddDays(7);
            // 首周若落到上一年，标签仍锚定到区间起点，避免出现「12/29」这类误导刻度。
            var labelDate = current < rangeStartDate ? rangeStartDate : current;
            var slot = new ChartSlot
            {
                Label = labelDate.ToDateTime(TimeOnly.MinValue).ToString("M/d"),
                RangeStart = LocalizationService.WeekStartLabel(current.ToDateTime(TimeOnly.MinValue)),
                RangeEnd = LocalizationService.FullDate(weekEnd.AddDays(-1).ToDateTime(TimeOnly.MinValue)),
            };
            slots.Add(slot);

            for (var day = current; day < weekEnd && day < rangeEndDate; day = day.AddDays(1))
            {
                if (!lookup.TryGetValue(day, out var point))
                {
                    continue;
                }

                foreach (var app in point.Apps)
                {
                    var key = app.ApplicationId.ToString("N");
                    names[key] = app.Name;
                    colors[key] = app.ColorHex;
                    slot.Values[key] = slot.Values.GetValueOrDefault(key) + app.Seconds;
                    totals[key] = totals.GetValueOrDefault(key) + app.Seconds;
                }
            }
        }

        return new ChartData
        {
            Series = BuildSeries(totals, names, colors),
            Slots = slots,
            Unit = ChartValueUnit.Hours,
            // 周是本次分箱的最小单位，不再把相邻周合并成半个月。
            GroupingSteps = [1],
        };
    }

    /// <summary>取日期所在周的周一（与统计口径一致，周一起始）。</summary>
    private static DateOnly StartOfWeek(DateOnly date)
    {
        var difference = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-difference);
    }

    /// <summary>按总时长降序排列系列，使堆叠顺序在所有柱子里保持一致。</summary>
    private static IReadOnlyList<ChartSeries> BuildSeries(        IReadOnlyDictionary<string, long> totals,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, string> colors) =>
        totals
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => names.GetValueOrDefault(pair.Key), StringComparer.CurrentCulture)
            .Select(pair => new ChartSeries
            {
                Key = pair.Key,
                Name = names.GetValueOrDefault(pair.Key) ?? LocalizationService.T("Loc.Chart.Unmatched"),
                Color = ParseColor(colors.GetValueOrDefault(pair.Key)),
                TotalSeconds = pair.Value,
            })
            .ToList();

    private static Color ParseColor(string? colorHex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(
                AppColorPalette.Resolve(colorHex));
        }
        catch
        {
            return Colors.SlateGray;
        }
    }
}
