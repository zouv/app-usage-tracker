namespace AppUsageTracker.Services;

public interface IIdleStateMonitor : IDisposable
{
    event EventHandler<bool>? IdleStateChanged;

    bool IsIdle { get; }

    TimeSpan IdleDuration { get; }

    int ThresholdMinutes { get; set; }

    void Start();

    void Stop();
}
