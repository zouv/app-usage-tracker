using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IProcessScanner
{
    /// <summary>枚举当前正在运行的进程（不要求有可见窗口），供「运行」统计模式匹配。</summary>
    IReadOnlyList<RunningProcessInfo> EnumerateRunningProcesses();

    IReadOnlyList<RunningProcessInfo> EnumerateVisibleProcesses();
}
