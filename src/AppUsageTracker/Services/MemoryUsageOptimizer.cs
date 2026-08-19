using System.Diagnostics;
using System.Windows.Threading;

namespace AppUsageTracker.Services;

public static class MemoryUsageOptimizer
{
    public static void ScheduleTrim()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TrimWorkingSet();
        };
        timer.Start();
    }

    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Optimized, false, true);
            using var process = Process.GetCurrentProcess();
            _ = NativeMethods.SetProcessWorkingSetSize(
                process.Handle,
                new nint(-1),
                new nint(-1));
        }
        catch
        {
            // 工作集回收属于可选优化，失败不影响功能。
        }
    }
}
