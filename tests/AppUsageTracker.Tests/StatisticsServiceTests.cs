using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class StatisticsServiceTests
{
    private readonly StatisticsService _service = new();

    [Fact]
    public void WeekStatisticsBuildRankingDailyPointsAndPreviousChange()
    {
        var appA = new TrackedApp { Name = "A" };
        var appB = new TrackedApp { Name = "B" };
        var monday = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Local);
        var sessions = new[]
        {
            Session(appA.Id, monday, 3600),
            Session(appB.Id, monday.AddDays(1), 1800),
            Session(appA.Id, monday.AddDays(-7), 1800),
        };

        var result = _service.Build(
            StatisticsPeriod.Week,
            monday,
            sessions,
            [appA, appB]);

        Assert.Equal(5400, result.TotalSeconds);
        Assert.Equal(appA.Id, result.TopApp?.ApplicationId);
        Assert.Equal(2, result.DailyPoints.Count);
        Assert.Equal(200, result.PreviousPeriodChangePercentage);
    }

    [Fact]
    public void MonthRangeExcludesSessionsOutsideMonth()
    {
        var app = new TrackedApp { Name = "Editor" };
        var anchor = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var sessions = new[]
        {
            Session(app.Id, new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Local), 100),
            Session(app.Id, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local), 200),
        };

        var result = _service.Build(StatisticsPeriod.Month, anchor, sessions, [app]);

        Assert.Equal(100, result.TotalSeconds);
    }

    [Fact]
    public void LeapDayIsIncludedInYearStatistics()
    {
        var app = new TrackedApp { Name = "Editor" };
        var leapDay = new DateTime(2024, 2, 29, 10, 0, 0, DateTimeKind.Local);

        var result = _service.Build(
            StatisticsPeriod.Year,
            leapDay,
            [Session(app.Id, leapDay, 2400)],
            [app]);

        Assert.Equal(2400, result.TotalSeconds);
        Assert.Equal(new DateOnly(2024, 2, 29), Assert.Single(result.DailyPoints).Date);
    }

    [Fact]
    public void PreviousMonthChangeUsesPreviousCalendarMonth()
    {
        var app = new TrackedApp { Name = "Editor" };
        var anchor = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Local);

        var result = _service.Build(
            StatisticsPeriod.Month,
            anchor,
            [
                Session(app.Id, new DateTime(2026, 2, 28, 10, 0, 0, DateTimeKind.Local), 100),
                Session(app.Id, new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Local), 150),
            ],
            [app]);

        Assert.Equal(50, result.PreviousPeriodChangePercentage);
    }

    private static ActivitySession Session(Guid appId, DateTime localStart, long seconds) =>
        new()
        {
            ApplicationId = appId,
            StartedAtUtc = localStart.ToUniversalTime(),
            EndedAtUtc = localStart.AddSeconds(seconds).ToUniversalTime(),
            DurationSeconds = seconds,
            LastHeartbeatAtUtc = localStart.AddSeconds(seconds).ToUniversalTime(),
        };
}
