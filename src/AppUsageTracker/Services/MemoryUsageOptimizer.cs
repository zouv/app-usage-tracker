using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace AppUsageTracker.Services;

/// <summary>
/// 托盘常驻时的工作集回收。延迟到窗口隐藏一段时间后执行，
/// 避免每次呼出/隐藏都触发完整 GC 与工作集裁剪，反而造成 UI 卡顿和后续缺页。
/// </summary>
public static class MemoryUsageOptimizer
{
    private static DispatcherTimer? _pendingTimer;

    public static void ScheduleTrim()
    {
        var app = Application.Current;
        if (app is null || _pendingTimer is not null)
        {
            return;
        }

        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(
            TimeSpan.FromSeconds(60),
            DispatcherPriority.Background,
            (_, _) =>
            {
                timer?.Stop();
                _pendingTimer = null;
                if (app.MainWindow?.IsVisible != true)
                {
                    TrimWorkingSet();
                }
            },
            app.Dispatcher);
        _pendingTimer = timer;
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
