namespace AppUsageTracker.Services;

public interface ITimeProvider
{
    DateTime UtcNow { get; }

    long GetTimestamp();

    double GetElapsedSeconds(long startTimestamp, long endTimestamp);
}
