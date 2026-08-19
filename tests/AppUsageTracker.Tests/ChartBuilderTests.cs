using AppUsageTracker.Models;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Tests;

public class ChartBuilderTests
{
    private static readonly DateTime Day = new(2026, 8, 13, 0, 0, 0, DateTimeKind.Local);

    [Fact]
    public void BuildTimeline_ReturnsEmptyWhenNoSessionsToday()
    {
        var app = new TrackedApp { Name = "编辑器" };
        var yesterday = Session(app.Id, Day.AddDays(-1).AddHours(10), 600);

        var chart = ChartBuilder.BuildTimeline([yesterday], [app], Day);

        Assert.False(chart.HasData);
        Assert.Empty(chart.Slots);
    }

    [Fact]
    public void BuildTimeline_SplitsSessionAcrossMinuteBins()
    {
        var app = new TrackedApp { Name = "编辑器" };
        // 10:00 起连续 3 分钟，应落进 3 个分箱，每箱 60 秒。
        var session = Session(app.Id, Day.AddHours(10), 180);

        var chart = ChartBuilder.BuildTimeline([session], [app], Day);
        var key = app.Id.ToString("N");
        var filled = chart.Slots.Where(slot => slot.TotalSeconds > 0).ToList();

        Assert.Equal(3, filled.Count);
        Assert.All(filled, slot => Assert.Equal(60, slot.Values[key]));
        Assert.Equal("10:00", filled[0].RangeStart);
        Assert.Equal("10:03", filled[^1].RangeEnd);
    }

    [Fact]
    public void BuildTimeline_TotalMatchesSessionDuration()
    {
        var app = new TrackedApp { Name = "浏览器" };
        // 起止不对齐整分钟，摊分后总量仍应守恒。
        var session = Session(app.Id, Day.AddHours(9).AddSeconds(80), 400);

        var chart = ChartBuilder.BuildTimeline([session], [app], Day);

        Assert.Equal(400, chart.Slots.Sum(slot => slot.TotalSeconds));
        Assert.Equal(400, chart.Series.Single().TotalSeconds);
    }

    [Fact]
    public void BuildTimeline_OrdersSeriesByTotalDescending()
    {
        var small = new TrackedApp { Name = "小" };
        var large = new TrackedApp { Name = "大" };
        var sessions = new[]
        {
            Session(small.Id, Day.AddHours(10), 120),
            Session(large.Id, Day.AddHours(11), 600),
        };

        var chart = ChartBuilder.BuildTimeline(sessions, [small, large], Day);

        Assert.Equal(large.Id.ToString("N"), chart.Series[0].Key);
        Assert.Equal(small.Id.ToString("N"), chart.Series[1].Key);
    }

    [Fact]
    public void BuildTimeline_LabelsUnmatchedSessionsSeparately()
    {
        var session = Session(null, Day.AddHours(14), 120);

        var chart = ChartBuilder.BuildTimeline([session], [], Day);

        Assert.Equal("未匹配软件", chart.Series.Single().Name);
    }

    [Fact]
    public void BuildDaily_FillsGapDaysSoSpacingMatchesCalendar()
    {
        var app = new TrackedApp { Name = "编辑器" };
        var points = new[]
        {
            Point(new DateOnly(2026, 8, 10), app, 3600),
            Point(new DateOnly(2026, 8, 13), app, 1800),
        };

        var chart = ChartBuilder.BuildDaily(points, new DateTime(2026, 8, 10), new DateTime(2026, 8, 14));

        // 10 号到 13 号共 4 天，中间两天补空柱。
        Assert.Equal(4, chart.Slots.Count);
        Assert.Equal(0, chart.Slots[1].TotalSeconds);
        Assert.Equal(0, chart.Slots[2].TotalSeconds);
        Assert.Equal(3600, chart.Slots[0].TotalSeconds);
        Assert.Equal(1800, chart.Slots[3].TotalSeconds);
    }

    [Fact]
    public void BuildDaily_UsesDataRangeForOpenEndedPeriod()
    {
        var app = new TrackedApp { Name = "编辑器" };
        var points = new[]
        {
            Point(new DateOnly(2026, 8, 1), app, 600),
            Point(new DateOnly(2026, 8, 3), app, 600),
        };

        var chart = ChartBuilder.BuildDaily(points, DateTime.MinValue, DateTime.MaxValue);

        Assert.Equal(3, chart.Slots.Count);
        Assert.Equal("小时", chart.ValueUnit);
    }

    [Fact]
    public void BuildDaily_AccumulatesSameAppAcrossDaysIntoOneSeries()
    {
        var app = new TrackedApp { Name = "编辑器" };
        var points = new[]
        {
            Point(new DateOnly(2026, 8, 10), app, 3600),
            Point(new DateOnly(2026, 8, 11), app, 1800),
        };

        var chart = ChartBuilder.BuildDaily(points, new DateTime(2026, 8, 10), new DateTime(2026, 8, 12));

        Assert.Equal(5400, chart.Series.Single().TotalSeconds);
    }

    [Fact]
    public void BuildDaily_ReturnsEmptyForNoPoints()
    {
        var chart = ChartBuilder.BuildDaily([], new DateTime(2026, 8, 10), new DateTime(2026, 8, 12));

        Assert.False(chart.HasData);
    }

    [Fact]
    public void BuildByApp_CreatesOneBarPerAppOrderedByTotalDescending()
    {
        var small = new TrackedApp { Name = "小" };
        var large = new TrackedApp { Name = "大" };
        var points = new[]
        {
            Point(new DateOnly(2026, 8, 10), large, 3600),
            Point(new DateOnly(2026, 8, 11), small, 1200),
        };

        var chart = ChartBuilder.BuildByApp(points);

        Assert.Equal(2, chart.Slots.Count);
        Assert.Equal(2, chart.Series.Count);
        Assert.Equal(large.Id.ToString("N"), chart.Series[0].Key);
        Assert.Equal(small.Id.ToString("N"), chart.Series[1].Key);
        Assert.Equal(large.Id.ToString("N"), chart.Slots[0].Values.Keys.Single());
        Assert.Equal(3600, chart.Slots[0].TotalSeconds);
        Assert.Equal(small.Id.ToString("N"), chart.Slots[1].Values.Keys.Single());
        Assert.Equal(1200, chart.Slots[1].TotalSeconds);
        Assert.Single(chart.GroupingSteps);
        Assert.Equal(1, chart.GroupingSteps[0]);
    }

    [Fact]
    public void BuildByApp_ReturnsEmptyForNoPoints()
    {
        Assert.False(ChartBuilder.BuildByApp([]).HasData);
    }

    [Fact]
    public void BuildWeekly_GroupsDaysIntoMondayAlignedWeeks()
    {
        var app = new TrackedApp { Name = "编辑器" };
        var monday = new DateTime(2026, 8, 10);
        var points = new[]
        {
            Point(new DateOnly(2026, 8, 10), app, 3600),
            Point(new DateOnly(2026, 8, 12), app, 3600),
            Point(new DateOnly(2026, 8, 17), app, 1800),
        };

        var chart = ChartBuilder.BuildWeekly(points, monday, monday.AddDays(8));

        Assert.Equal(2, chart.Slots.Count);
        Assert.Equal(7200, chart.Slots[0].TotalSeconds);
        Assert.Equal(1800, chart.Slots[1].TotalSeconds);
        Assert.Equal("8/10", chart.Slots[0].Label);
        Assert.Equal("8/17", chart.Slots[1].Label);
        Assert.Equal(9000, chart.Series.Single().TotalSeconds);
    }

    [Fact]
    public void BuildWeekly_ClampsFirstWeekLabelToRangeStart()
    {
        var app = new TrackedApp { Name = "编辑器" };
        var wednesday = new DateTime(2026, 8, 12);

        var chart = ChartBuilder.BuildWeekly(
            [Point(new DateOnly(2026, 8, 12), app, 600)],
            wednesday,
            wednesday.AddDays(7));

        Assert.Equal("8/12", chart.Slots[0].Label);
        Assert.Equal(600, chart.Slots[0].TotalSeconds);
    }

    [Fact]
    public void BuildWeekly_ReturnsEmptyForNoPoints()
    {
        Assert.False(ChartBuilder.BuildWeekly([], new DateTime(2026, 8, 10), new DateTime(2026, 8, 17)).HasData);
    }

    private static ActivitySession Session(Guid? appId, DateTime startLocal, long seconds) =>
        new()
        {
            ApplicationId = appId,
            StartedAtUtc = startLocal.ToUniversalTime(),
            EndedAtUtc = startLocal.AddSeconds(seconds).ToUniversalTime(),
            DurationSeconds = seconds,
            State = ActivityState.Active,
        };

    private static DailyUsagePoint Point(DateOnly date, TrackedApp app, long seconds) =>
        new(
            date,
            seconds,
            [new AppUsageSummary(app.Id, app.Name, app.ColorHex, seconds, 100)]);
}
