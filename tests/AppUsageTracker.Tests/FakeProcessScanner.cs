using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

internal sealed class FakeProcessScanner : IProcessScanner
{
    public List<RunningProcessInfo> Running { get; set; } = [];

    public IReadOnlyList<RunningProcessInfo> EnumerateRunningProcesses(
        IReadOnlyCollection<string>? processNames = null) => Running;

    public IReadOnlyList<RunningProcessInfo> EnumerateVisibleProcesses() => [];
}
