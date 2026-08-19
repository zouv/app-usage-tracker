namespace AppUsageTracker.Models;

public sealed class DailyAggregate
{
    public DateOnly Date { get; set; }

    public Guid ApplicationId { get; set; }

    public long EffectiveSeconds { get; set; }

    public long ForegroundSeconds { get; set; }

    public long RunningSeconds { get; set; }
}
