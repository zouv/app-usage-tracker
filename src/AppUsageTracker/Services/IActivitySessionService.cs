using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed record TrackingSnapshot(
    ActivityState State,
    TrackedApp? CurrentApp,
    ForegroundWindowInfo? CurrentWindow,
    ActivitySession? CurrentSession,
    bool IsPaused,
    bool IsPrivateMode);

public interface IActivitySessionService : IDisposable
{
    event EventHandler<TrackingSnapshot>? SnapshotChanged;

    TrackingSnapshot Snapshot { get; }

    IReadOnlyList<ActivitySession> Sessions { get; }

    Task StartAsync(AppDataState state, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task HandleForegroundWindowAsync(
        ForegroundWindowInfo? window,
        CancellationToken cancellationToken = default);

    /// <summary>心跳时同步当前运行进程列表，驱动「运行」统计模式的会话开始/结束。</summary>
    Task HandleRunningProcessesAsync(
        IReadOnlyCollection<RunningProcessInfo> processes,
        CancellationToken cancellationToken = default);

    Task SetIdleAsync(bool isIdle, CancellationToken cancellationToken = default);

    Task SetSystemStateAsync(
        SystemSessionState state,
        CancellationToken cancellationToken = default);

    Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default);

    Task SetPrivateModeAsync(bool enabled, CancellationToken cancellationToken = default);

    Task HeartbeatAsync(CancellationToken cancellationToken = default);
}
