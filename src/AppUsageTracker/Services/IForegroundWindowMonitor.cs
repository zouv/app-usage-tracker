using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IForegroundWindowMonitor : IDisposable
{
    event EventHandler<ForegroundWindowInfo?>? ForegroundWindowChanged;

    ForegroundWindowInfo? Current { get; }

    void Start();

    void Stop();
}
