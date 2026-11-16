using System.Windows.Threading;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Tests;

/// <summary>
/// 概览刷新排队的线程亲和约束：MainWindow 归 UI 线程所有，
/// 后台监听线程只能排队，可见性探测必须留到调度器回调里做。
/// </summary>
public sealed class OverviewRefreshTests
{
    [Fact]
    public void BackgroundThreadQueuesWithoutProbingMainWindow()
    {
        var (dispatcher, thread) = StartDispatcherThread();
        try
        {
            // 回归 1.3.0 缺陷：后台线程直接读主窗口会抛跨线程异常，概览每秒增量更新随之失效。
            var skip = OverviewViewModel.ShouldSkipRefresh(
                dispatcher,
                isActive: true,
                isWindowVisible: () => throw new InvalidOperationException("后台线程探测了主窗口可见性。"));
            Assert.False(skip);
        }
        finally
        {
            StopDispatcherThread(dispatcher, thread);
        }
    }

    [Fact]
    public void UiThreadSkipsRefreshWhenWindowHiddenOrMinimized()
    {
        var (dispatcher, thread) = StartDispatcherThread();
        try
        {
            dispatcher.Invoke(() =>
            {
                Assert.True(OverviewViewModel.ShouldSkipRefresh(dispatcher, isActive: true, () => false));
                Assert.False(OverviewViewModel.ShouldSkipRefresh(dispatcher, isActive: true, () => true));
            });
        }
        finally
        {
            StopDispatcherThread(dispatcher, thread);
        }
    }

    [Fact]
    public void InactivePageSkipsRefreshWithoutProbingMainWindow()
    {
        var skip = OverviewViewModel.ShouldSkipRefresh(
            Dispatcher.CurrentDispatcher,
            isActive: false,
            isWindowVisible: () => throw new InvalidOperationException("离开概览页后仍探测了主窗口可见性。"));
        Assert.True(skip);
    }

    /// <summary>起一条独立调度器线程，模拟应用 UI 线程；测试线程相对它就是后台监听线程。</summary>
    private static (Dispatcher Dispatcher, Thread Thread) StartDispatcherThread()
    {
        Dispatcher? captured = null;
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            captured = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "OverviewRefreshTestDispatcher",
        };
        thread.Start();
        ready.Wait();
        return (captured ?? throw new InvalidOperationException("测试调度器线程未就绪。"), thread);
    }

    private static void StopDispatcherThread(Dispatcher dispatcher, Thread thread)
    {
        dispatcher.InvokeShutdown();
        thread.Join(TimeSpan.FromSeconds(5));
    }
}