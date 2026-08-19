using Timer = System.Timers.Timer;

namespace AppUsageTracker.Services;

public sealed class IdleStateMonitor : IIdleStateMonitor
{
    private readonly Timer _timer = new(1000) { AutoReset = true };
    private bool _disposed;

    public IdleStateMonitor()
    {
        _timer.Elapsed += (_, _) => Poll();
    }

    public event EventHandler<bool>? IdleStateChanged;

    public bool IsIdle { get; private set; }

    public TimeSpan IdleDuration { get; private set; }

    public int ThresholdMinutes { get; set; } = 5;

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void Poll()
    {
        var info = new NativeMethods.LastInputInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LastInputInfo>(),
        };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return;
        }

        var elapsedMilliseconds = NativeMethods.GetTickCount64() - info.Time;
        IdleDuration = TimeSpan.FromMilliseconds(elapsedMilliseconds);
        var idle = IdleDuration >= TimeSpan.FromMinutes(Math.Max(1, ThresholdMinutes));
        if (idle == IsIdle)
        {
            return;
        }

        IsIdle = idle;
        IdleStateChanged?.Invoke(this, idle);
    }
}
