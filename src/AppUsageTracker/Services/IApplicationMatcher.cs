using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IApplicationMatcher
{
    TrackedApp? Match(ForegroundWindowInfo window, IReadOnlyCollection<TrackedApp> apps);

    /// <summary>从正在运行的进程集合里匹配「运行」统计模式的软件（忽略窗口标题类规则）。</summary>
    TrackedApp? MatchRunningProcess(
        IReadOnlyCollection<RunningProcessInfo> processes,
        IReadOnlyCollection<TrackedApp> apps);
}
