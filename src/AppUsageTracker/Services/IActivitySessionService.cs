using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

/// <summary>一个正在累计的活跃软件：软件配置、进行中的会话与触发其累计的来源模式。</summary>
public sealed record ActiveAppInfo(
    TrackedApp App,
    ActivitySession Session,
    TrackingMode SourceMode)
{
    /// <summary>
    /// 估算当前连续时长：会话快照里的已计时长 + 距最后一次心跳的增量。
    /// 直接拿当前时间减 StartedAt 会把短切换合并留下的空档算进去。
    /// </summary>
    public long EstimateDurationSeconds(DateTime nowUtc)
    {
        var extra = nowUtc > Session.LastHeartbeatAtUtc
            ? (long)(nowUtc - Session.LastHeartbeatAtUtc).TotalSeconds
            : 0;
        return Session.DurationSeconds + Math.Max(0, extra);
    }
}

public sealed record TrackingSnapshot(
    ActivityState State,
    TrackedApp? CurrentApp,
    ForegroundWindowInfo? CurrentWindow,
    ActivitySession? CurrentSession,
    bool IsPaused,
    bool IsPrivateMode,
    IReadOnlyList<ActiveAppInfo> ActiveApps);

public interface IActivitySessionService : IDisposable
{
    event EventHandler<TrackingSnapshot>? SnapshotChanged;

    TrackingSnapshot Snapshot { get; }

    /// <summary>会话快照（深拷贝）。调用方可以安全地在任意线程枚举，不受后台会话变更影响。</summary>
    IReadOnlyList<ActivitySession> Sessions { get; }

    /// <summary>是否存在需要定时扫描进程的「运行」模式软件；暂停/锁屏/休眠时返回 false。</summary>
    bool ShouldScanRunningProcesses { get; }

    /// <summary>监听线程使用的软件配置快照，后台统计/通知可安全枚举。</summary>
    IReadOnlyList<TrackedApp> AppsSnapshot { get; }

    /// <summary>运行模式扫描需要关注的进程名集合；扫描器据此跳过无关进程。</summary>
    IReadOnlyCollection<string> RunningProcessScanNames { get; }

    Task StartAsync(AppDataState state, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>停止监听但不写盘，供「恢复备份后立即退出」等场景使用。</summary>
    Task StopWithoutSavingAsync(CancellationToken cancellationToken = default);

    /// <summary>恢复备份失败后继续使用当前内存状态监听，不重新加载或覆盖磁盘数据。</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>软件配置变化后刷新匹配缓存，并结束已删除/停用软件的进行中会话。</summary>
    Task UpdateAppsAsync(
        IReadOnlyList<TrackedApp> apps,
        CancellationToken cancellationToken = default);

    /// <summary>在监听锁内修改会话集合，避免与心跳/前台事件并发写 List。</summary>
    Task<T> EditSessionsAsync<T>(
        Func<List<ActivitySession>, T> edit,
        CancellationToken cancellationToken = default);

    /// <summary>清空全部原始会话（包括进行中的会话），用于设置页「清空历史」。</summary>
    Task ClearSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>设置变化后重新评估当前空闲/阻断状态，使排除空闲等开关立即生效。</summary>
    Task ReevaluateAsync(CancellationToken cancellationToken = default);

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
