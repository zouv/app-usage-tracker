using Timer = System.Timers.Timer;

namespace AppUsageTracker.Services;

public sealed class TrackingCoordinator : IDisposable
{
    private readonly IForegroundWindowMonitor _foregroundMonitor;
    private readonly IIdleStateMonitor _idleMonitor;
    private readonly ISystemSessionMonitor _sessionMonitor;
    private readonly IActivitySessionService _activityService;
    private readonly Timer _heartbeatTimer;
    private bool _started;

    public TrackingCoordinator(
        IForegroundWindowMonitor foregroundMonitor,
        IIdleStateMonitor idleMonitor,
        ISystemSessionMonitor sessionMonitor,
        IActivitySessionService activityService,
        int heartbeatSeconds)
    {
        _foregroundMonitor = foregroundMonitor;
        _idleMonitor = idleMonitor;
        _sessionMonitor = sessionMonitor;
        _activityService = activityService;
        _heartbeatTimer = new Timer(Math.Max(5, heartbeatSeconds) * 1000)
        {
            AutoReset = true,
        };
        _heartbeatTimer.Elapsed += async (_, _) => await SafeInvokeAsync(
            async cancellationToken =>
            {
                await _activityService.SetIdleAsync(CalculateIdleState(), cancellationToken);
                await _activityService.HeartbeatAsync(cancellationToken);
            });
    }

    public int HeartbeatSeconds
    {
        get => (int)Math.Round(_heartbeatTimer.Interval / 1000);
        set => _heartbeatTimer.Interval = Math.Max(5, value) * 1000;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _foregroundMonitor.ForegroundWindowChanged += OnForegroundChanged;
        _idleMonitor.IdleStateChanged += OnIdleChanged;
        _sessionMonitor.StateChanged += OnSessionStateChanged;
        _foregroundMonitor.Start();
        _idleMonitor.Start();
        _sessionMonitor.Start();
        _heartbeatTimer.Start();
    }

    public void Dispose()
    {
        _heartbeatTimer.Stop();
        _heartbeatTimer.Dispose();
        _foregroundMonitor.ForegroundWindowChanged -= OnForegroundChanged;
        _idleMonitor.IdleStateChanged -= OnIdleChanged;
        _sessionMonitor.StateChanged -= OnSessionStateChanged;
        _foregroundMonitor.Dispose();
        _idleMonitor.Dispose();
        _sessionMonitor.Dispose();
    }

    private async void OnForegroundChanged(object? sender, Models.ForegroundWindowInfo? window) =>
        await SafeInvokeAsync(
            async cancellationToken =>
            {
                await _activityService.HandleForegroundWindowAsync(window, cancellationToken);
                await _activityService.SetIdleAsync(CalculateIdleState(), cancellationToken);
            });

    private async void OnIdleChanged(object? sender, bool isIdle) =>
        await SafeInvokeAsync(
            cancellationToken => _activityService.SetIdleAsync(CalculateIdleState(), cancellationToken));

    private async void OnSessionStateChanged(object? sender, SystemSessionState state) =>
        await SafeInvokeAsync(
            cancellationToken => _activityService.SetSystemStateAsync(state, cancellationToken));

    private static async Task SafeInvokeAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception exception)
        {
            AppLogger.Error("后台监听事件处理失败。", exception);
        }
    }

    private bool CalculateIdleState()
    {
        var app = _activityService.Snapshot.CurrentApp;
        if (app?.IgnoreIdle == true)
        {
            return false;
        }

        return app?.IdleThresholdMinutes is { } customThreshold
            ? _idleMonitor.IdleDuration >= TimeSpan.FromMinutes(Math.Max(1, customThreshold))
            : _idleMonitor.IsIdle;
    }
}
