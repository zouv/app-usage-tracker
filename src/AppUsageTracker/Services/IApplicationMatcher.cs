using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IApplicationMatcher
{
    TrackedApp? Match(ForegroundWindowInfo window, IReadOnlyCollection<TrackedApp> apps);
}
