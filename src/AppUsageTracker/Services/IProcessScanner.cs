using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IProcessScanner
{
    IReadOnlyList<RunningProcessInfo> EnumerateVisibleProcesses();
}
