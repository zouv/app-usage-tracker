namespace AppUsageTracker.Services;

public enum SystemSessionState
{
    Available,
    Locked,
    Suspended,
}

public interface ISystemSessionMonitor : IDisposable
{
    event EventHandler<SystemSessionState>? StateChanged;

    SystemSessionState State { get; }

    void Start();

    void Stop();
}
