using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IStatisticsService
{
    StatisticsSnapshot Build(
        StatisticsPeriod period,
        DateTime anchorLocal,
        IReadOnlyCollection<ActivitySession> sessions,
        IReadOnlyCollection<TrackedApp> apps);

    Task<StatisticsSnapshot> BuildAsync(
        StatisticsPeriod period,
        DateTime anchorLocal,
        IReadOnlyCollection<ActivitySession> sessions,
        IReadOnlyCollection<TrackedApp> apps,
        CancellationToken cancellationToken = default);
}
