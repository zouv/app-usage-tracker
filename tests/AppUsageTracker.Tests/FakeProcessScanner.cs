using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

internal sealed class FakeProcessScanner : IProcessScanner
{
    public IReadOnlyList<RunningProcessInfo> EnumerateVisibleProcesses() => [];
}
