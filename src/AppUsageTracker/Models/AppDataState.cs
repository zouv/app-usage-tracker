namespace AppUsageTracker.Models;

public sealed class AppDataState
{
    public AppSettings Settings { get; set; } = new();

    public List<TrackedApp> Apps { get; set; } = [];

    public List<ActivitySession> Sessions { get; set; } = [];

    public List<DailyAggregate> Aggregates { get; set; } = [];

    public List<ManualCorrection> Corrections { get; set; } = [];
}
