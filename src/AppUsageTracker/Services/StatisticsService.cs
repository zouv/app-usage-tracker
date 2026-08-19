using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class StatisticsService : IStatisticsService
{
    public Task<StatisticsSnapshot> BuildAsync(
        StatisticsPeriod period,
        DateTime anchorLocal,
        IReadOnlyCollection<ActivitySession> sessions,
        IReadOnlyCollection<TrackedApp> apps,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Build(period, anchorLocal, sessions, apps);
            },
            cancellationToken);

    public StatisticsSnapshot Build(
        StatisticsPeriod period,
        DateTime anchorLocal,
        IReadOnlyCollection<ActivitySession> sessions,
        IReadOnlyCollection<TrackedApp> apps)
    {
        var (start, end) = GetRange(period, anchorLocal);
        var previousStart = GetPreviousStart(period, start);
        var previousEnd = start;
        var current = SelectSegments(sessions, start, end).ToList();
        var previous = SelectSegments(sessions, previousStart, previousEnd).ToList();
        var appMap = apps.ToDictionary(app => app.Id);
        var total = current.Sum(item => item.Seconds);
        var previousTotal = previous.Sum(item => item.Seconds);
        var dayCount = period == StatisticsPeriod.All
            ? Math.Max(1, current.Select(item => item.StartLocal.Date).Distinct().Count())
            : Math.Max(1, (end.Date - start.Date).Days);

        var ranking = current
            .Where(item => item.ApplicationId is not null)
            .GroupBy(item => item.ApplicationId!.Value)
            .Select(group =>
            {
                var seconds = group.Sum(item => item.Seconds);
                var app = appMap.GetValueOrDefault(group.Key);
                return new AppUsageSummary(
                    group.Key,
                    app?.Name ?? "已删除软件",
                    app?.ColorHex ?? "#7A8491",
                    seconds,
                    total == 0 ? 0 : seconds * 100d / total);
            })
            .OrderByDescending(item => item.Seconds)
            .ToList();

        var daily = current
            .GroupBy(item => DateOnly.FromDateTime(item.StartLocal))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var dayTotal = group.Sum(item => item.Seconds);
                var summaries = group
                    .Where(item => item.ApplicationId is not null)
                    .GroupBy(item => item.ApplicationId!.Value)
                    .Select(appGroup =>
                    {
                        var seconds = appGroup.Sum(item => item.Seconds);
                        var app = appMap.GetValueOrDefault(appGroup.Key);
                        return new AppUsageSummary(
                            appGroup.Key,
                            app?.Name ?? "已删除软件",
                            app?.ColorHex ?? "#7A8491",
                            seconds,
                            dayTotal == 0 ? 0 : seconds * 100d / dayTotal);
                    })
                    .OrderByDescending(item => item.Seconds)
                    .ToList();
                return new DailyUsagePoint(group.Key, dayTotal, summaries);
            })
            .ToList();

        return new StatisticsSnapshot(
            start,
            end,
            total,
            total / dayCount,
            ranking.FirstOrDefault(),
            current.Count == 0 ? 0 : current.Max(item => item.Seconds),
            previousTotal == 0
                ? total > 0 ? 100 : 0
                : (total - previousTotal) * 100d / previousTotal,
            ranking,
            daily);
    }

    private static (DateTime Start, DateTime End) GetRange(
        StatisticsPeriod period,
        DateTime anchor)
    {
        var day = anchor.Date;
        return period switch
        {
            StatisticsPeriod.Day => (day, day.AddDays(1)),
            StatisticsPeriod.Week => (StartOfWeek(day), StartOfWeek(day).AddDays(7)),
            StatisticsPeriod.Month => (
                new DateTime(day.Year, day.Month, 1),
                new DateTime(day.Year, day.Month, 1).AddMonths(1)),
            StatisticsPeriod.Year => (
                new DateTime(day.Year, 1, 1),
                new DateTime(day.Year + 1, 1, 1)),
            StatisticsPeriod.All => (DateTime.MinValue, DateTime.MaxValue),
            _ => (day, day.AddDays(1)),
        };
    }

    private static DateTime GetPreviousStart(StatisticsPeriod period, DateTime start) =>
        period switch
        {
            StatisticsPeriod.Day => start.AddDays(-1),
            StatisticsPeriod.Week => start.AddDays(-7),
            StatisticsPeriod.Month => start.AddMonths(-1),
            StatisticsPeriod.Year => start.AddYears(-1),
            StatisticsPeriod.All => DateTime.MinValue,
            _ => start.AddDays(-1),
        };

    private static DateTime StartOfWeek(DateTime date)
    {
        var difference = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-difference);
    }

    private static IEnumerable<SessionSegment> SelectSegments(
        IEnumerable<ActivitySession> sessions,
        DateTime startLocal,
        DateTime endLocal)
    {
        foreach (var session in sessions)
        {
            if (session.EndedAtUtc is null ||
                session.DurationSeconds <= 0 ||
                session.State != ActivityState.Active ||
                session.ApplicationId is null)
            {
                continue;
            }

            var localStart = session.StartedAtUtc.ToLocalTime();
            var localEnd = session.EndedAtUtc.Value.ToLocalTime();
            if (localEnd <= startLocal || localStart >= endLocal)
            {
                continue;
            }

            var clippedStart = localStart < startLocal ? startLocal : localStart;
            var clippedEnd = localEnd > endLocal ? endLocal : localEnd;
            var wallSeconds = Math.Max(1, (localEnd - localStart).TotalSeconds);
            var ratio = Math.Clamp((clippedEnd - clippedStart).TotalSeconds / wallSeconds, 0, 1);
            yield return new SessionSegment(
                session.ApplicationId,
                clippedStart,
                Math.Max(0, (long)Math.Round(session.DurationSeconds * ratio)));
        }
    }

    private sealed record SessionSegment(
        Guid? ApplicationId,
        DateTime StartLocal,
        long Seconds);
}
