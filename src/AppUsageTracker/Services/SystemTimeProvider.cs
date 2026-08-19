using System.Diagnostics;

namespace AppUsageTracker.Services;

public sealed class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public double GetElapsedSeconds(long startTimestamp, long endTimestamp) =>
        (endTimestamp - startTimestamp) / (double)Stopwatch.Frequency;
}
