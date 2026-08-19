namespace AppUsageTracker.Models;

public enum StatisticsPeriod
{
    Day,
    Week,
    Month,
    Year,
    All,
}

public sealed record AppUsageSummary(
    Guid ApplicationId,
    string Name,
    string ColorHex,
    long Seconds,
    double Percentage);

public sealed record DailyUsagePoint(
    DateOnly Date,
    long Seconds,
    IReadOnlyList<AppUsageSummary> Apps);

public sealed record StatisticsSnapshot(
    DateTime RangeStartLocal,
    DateTime RangeEndLocal,
    long TotalSeconds,
    long AverageDailySeconds,
    AppUsageSummary? TopApp,
    long LongestSessionSeconds,
    double PreviousPeriodChangePercentage,
    IReadOnlyList<AppUsageSummary> Ranking,
    IReadOnlyList<DailyUsagePoint> DailyPoints);
