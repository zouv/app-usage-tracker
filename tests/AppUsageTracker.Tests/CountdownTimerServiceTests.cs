using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

/// <summary>用假单调时钟驱动倒计时服务，验证开始/暂停/继续/停止/结束语义。</summary>
public sealed class CountdownTimerServiceTests
{
    private long _ticks;

    private CountdownTimerService Create() => new(() => _ticks, 1000, 600_000);

    [Fact]
    public void StartCountsDownWithMonotonicTicks()
    {
        using var service = Create();
        service.Start(TimeSpan.FromSeconds(10));

        _ticks += 3000;

        Assert.Equal(TimeSpan.FromSeconds(7), service.Remaining);
        Assert.True(service.IsRunning);
        Assert.True(service.IsActive);
    }

    [Fact]
    public void PauseFreezesAndResumeContinues()
    {
        using var service = Create();
        service.Start(TimeSpan.FromSeconds(10));
        _ticks += 2000;
        service.Pause();

        _ticks += 5000;
        Assert.Equal(TimeSpan.FromSeconds(8), service.Remaining);
        Assert.True(service.IsPaused);
        Assert.False(service.IsRunning);

        service.Resume();
        _ticks += 3000;
        Assert.Equal(TimeSpan.FromSeconds(5), service.Remaining);
    }

    [Fact]
    public void StopClearsStateWithoutFinished()
    {
        using var service = Create();
        var finished = 0;
        service.Finished += () => finished++;

        service.Start(TimeSpan.FromSeconds(10));
        service.Stop();

        Assert.False(service.IsActive);
        Assert.Equal(TimeSpan.Zero, service.Remaining);
        Assert.Equal(0, finished);
    }

    [Fact]
    public void TickFinishesOnceWhenRemainingReachesZero()
    {
        using var service = Create();
        var finished = 0;
        service.Finished += () => finished++;

        service.Start(TimeSpan.FromSeconds(10));
        _ticks += 9000;
        service.Tick();
        Assert.Equal(0, finished);
        Assert.True(service.IsRunning);

        _ticks += 1000;
        service.Tick();
        Assert.Equal(1, finished);
        Assert.False(service.IsActive);
        Assert.Equal(TimeSpan.Zero, service.Remaining);

        service.Tick();
        Assert.Equal(1, finished);
    }

    [Fact]
    public void RestartAfterFinishWorks()
    {
        using var service = Create();
        service.Start(TimeSpan.FromSeconds(1));
        _ticks += 1000;
        service.Tick();
        Assert.False(service.IsActive);

        service.Start(TimeSpan.FromSeconds(5));

        Assert.True(service.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(5), service.Remaining);
    }
}