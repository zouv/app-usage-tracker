using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IProcessScanner
{
    /// <summary>
    /// 枚举当前正在运行的进程（不要求有可见窗口），供「运行」统计模式匹配。
    /// <paramref name="processNames"/> 非 null 时只返回进程名命中的进程，避免为无关进程查询路径。
    /// </summary>
    IReadOnlyList<RunningProcessInfo> EnumerateRunningProcesses(
        IReadOnlyCollection<string>? processNames = null);

    IReadOnlyList<RunningProcessInfo> EnumerateVisibleProcesses();
}
