using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

internal sealed class FakeTimeProvider(DateTime utcNow) : ITimeProvider
{
    private long _timestamp;

    public DateTime UtcNow { get; private set; } = utcNow;

    public long GetTimestamp() => _timestamp;

    public double GetElapsedSeconds(long startTimestamp, long endTimestamp) =>
        endTimestamp - startTimestamp;

    public void Advance(TimeSpan duration)
    {
        UtcNow = UtcNow.Add(duration);
        _timestamp += (long)duration.TotalSeconds;
    }
}
