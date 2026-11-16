using System.Diagnostics;

namespace AppUsageTracker.Services;

/// <summary>
/// 桌面倒计时定时器：用单调时间源计算剩余时间，<see cref="System.Timers.Timer"/> 驱动刷新；
/// 锁屏/桌布等结束动作由调用方订阅 <see cref="Finished"/> 后在 UI 线程执行。
/// 事件可能来自后台线程，消费方须自行回 Dispatcher。
/// </summary>
public sealed class CountdownTimerService : IDisposable
{
    private readonly Func<long> _getTicks;
    private readonly long _ticksPerSecond;
    private readonly System.Timers.Timer _timer;
    private readonly object _gate = new();
    private long _totalTicks;
    private long _pausedElapsedTicks;
    private long _resumeTick;
    private bool _running;
    private bool _paused;
    private bool _disposed;

    public CountdownTimerService()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency, 500)
    {
    }

    /// <summary>测试 seam：注入假单调时钟与刷新间隔，测试可直接调用 <see cref="Tick"/>。</summary>
    internal CountdownTimerService(Func<long> getTicks, long ticksPerSecond, double tickIntervalMs)
    {
        _getTicks = getTicks;
        _ticksPerSecond = ticksPerSecond;
        _timer = new System.Timers.Timer(tickIntervalMs);
        _timer.Elapsed += (_, _) => Tick();
    }

    /// <summary>剩余时间或运行状态变化（开始/暂停/继续/停止/每秒刷新/结束）。</summary>
    public event Action? Changed;

    /// <summary>倒计时自然结束（停止不触发）。</summary>
    public event Action? Finished;

    public bool IsRunning
    {
        get { lock (_gate) { return _running; } }
    }

    public bool IsPaused
    {
        get { lock (_gate) { return _paused; } }
    }

    public bool IsActive
    {
        get { lock (_gate) { return _running || _paused; } }
    }

    /// <summary>剩余时间；未开始或已结束时为零。</summary>
    public TimeSpan Remaining
    {
        get
        {
            lock (_gate)
            {
                if (!_running && !_paused)
                {
                    return TimeSpan.Zero;
                }

                var elapsed = _pausedElapsedTicks + (_running ? _getTicks() - _resumeTick : 0);
                var remaining = Math.Max(0, _totalTicks - elapsed);
                return TimeSpan.FromSeconds((double)remaining / _ticksPerSecond);
            }
        }
    }

    /// <summary>以指定时长重新开始倒计时。</summary>
    public void Start(TimeSpan duration)
    {
        lock (_gate)
        {
            _totalTicks = Math.Max(0, (long)(duration.TotalSeconds * _ticksPerSecond));
            _pausedElapsedTicks = 0;
            _resumeTick = _getTicks();
            _running = true;
            _paused = false;
            _timer.Start();
        }

        Changed?.Invoke();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _pausedElapsedTicks += _getTicks() - _resumeTick;
            _running = false;
            _paused = true;
            _timer.Stop();
        }

        Changed?.Invoke();
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!_paused)
            {
                return;
            }

            _resumeTick = _getTicks();
            _paused = false;
            _running = true;
            _timer.Start();
        }

        Changed?.Invoke();
    }

    /// <summary>手动停止并清零，不触发 <see cref="Finished"/>。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _paused = false;
            _timer.Stop();
        }

        Changed?.Invoke();
    }

    internal void Tick()
    {
        var finished = false;
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            if (_totalTicks - (_pausedElapsedTicks + _getTicks() - _resumeTick) <= 0)
            {
                _running = false;
                _timer.Stop();
                finished = true;
            }
        }

        Changed?.Invoke();
        if (finished)
        {
            Finished?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }
}