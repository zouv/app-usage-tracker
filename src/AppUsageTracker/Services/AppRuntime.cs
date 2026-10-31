using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class AppRuntime : IAsyncDisposable
{
    private readonly IAppDataStore _store;
    private readonly ActivitySessionService _activityService;
    private readonly IdleStateMonitor _idleMonitor;
    private readonly TrackingCoordinator _coordinator;
    private bool _initialized;

    public AppRuntime(
        IAppDataStore store,
        ITimeProvider timeProvider,
        IApplicationMatcher matcher,
        IProcessScanner processScanner)
    {
        _store = store;
        TimeProvider = timeProvider;
        Matcher = matcher;
        ProcessScanner = processScanner;
        ForegroundMonitor = new ForegroundWindowMonitor();
        _idleMonitor = new IdleStateMonitor();
        SessionMonitor = new SystemSessionMonitor();
        _activityService = new ActivitySessionService(store, matcher, timeProvider);
        _activityService.SnapshotChanged += (_, snapshot) =>
        {
            SnapshotChanged?.Invoke(this, snapshot);
            DataChanged?.Invoke(this, EventArgs.Empty);
        };
        _coordinator = new TrackingCoordinator(
            ForegroundMonitor,
            _idleMonitor,
            SessionMonitor,
            _activityService,
            processScanner,
            15);
        Statistics = new StatisticsService();
    }

    public event EventHandler? DataChanged;

    public event EventHandler<TrackingSnapshot>? SnapshotChanged;

    public AppSettings Settings { get; private set; } = new();

    public List<TrackedApp> Apps { get; private set; } = [];

    /// <summary>原始会话快照（深拷贝）。后台线程可安全枚举，不受监听线程增删影响。</summary>
    public IReadOnlyList<ActivitySession> Sessions => _activityService.Sessions;

    /// <summary>仅供测试播种数据；业务侧读取请用 <see cref="Sessions"/> 或 <see cref="GetLiveSessionsSnapshot"/>。</summary>
    internal List<ActivitySession> RawSessions => _activityService.SessionList;

    /// <summary>监听服务持有的软件配置快照，供后台通知/统计安全枚举。</summary>
    public IReadOnlyList<TrackedApp> AppsSnapshot => _activityService.AppsSnapshot;

    public List<ManualCorrection> Corrections { get; private set; } = [];

    public List<DailyAggregate> Aggregates { get; private set; } = [];

    public ITimeProvider TimeProvider { get; }

    public IApplicationMatcher Matcher { get; }

    public IProcessScanner ProcessScanner { get; }

    public IForegroundWindowMonitor ForegroundMonitor { get; }

    public ISystemSessionMonitor SessionMonitor { get; }

    public IStatisticsService Statistics { get; }

    public IActivitySessionService ActivityService => _activityService;

    public TrackingSnapshot Snapshot => _activityService.Snapshot;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var state = await _store.LoadAsync(cancellationToken);
        Settings = state.Settings;
        Apps = state.Apps;
        Corrections = state.Corrections;
        Aggregates = state.Aggregates;

        // 历史数据里的软件多半共用同一个默认蓝，先按调色板槽位铺开再进入界面。
        if (AppColorPalette.AssignMissing(Apps))
        {
            await _store.SaveAppsAsync(Apps, cancellationToken);
        }

        _idleMonitor.ThresholdMinutes = Settings.IdleThresholdMinutes;
        _coordinator.HeartbeatSeconds = Settings.HeartbeatSeconds;
        await _activityService.StartAsync(state, cancellationToken);
        _coordinator.Start();
        _initialized = true;
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 返回用于实时展示的会话快照：已完成会话原样返回，进行中的软件会话按当前时长补上结束时间。
    /// 统计、通知和概览统一使用这一份口径，避免各处重复拼装。
    /// </summary>
    public IReadOnlyList<ActivitySession> GetLiveSessionsSnapshot()
    {
        var sessions = Sessions.ToList();
        var now = TimeProvider.UtcNow;
        foreach (var info in Snapshot.ActiveApps)
        {
            var existing = sessions.FirstOrDefault(item => item.Id == info.Session.Id);
            if (existing is null)
            {
                continue;
            }

            existing.EndedAtUtc = now;
            existing.DurationSeconds = info.EstimateDurationSeconds(now);
        }

        return sessions;
    }

    public async Task SaveAppsAsync(CancellationToken cancellationToken = default)
    {
        // 新增的软件可能还带着默认色，保存前统一补一次槽位。
        AppColorPalette.AssignMissing(Apps);
        await _store.SaveAppsAsync(Apps, cancellationToken);
        // 让监听服务立即看到最新配置：停用/删除软件时马上结束进行中的会话。
        await _activityService.UpdateAppsAsync(Apps, cancellationToken);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        _idleMonitor.ThresholdMinutes = Settings.IdleThresholdMinutes;
        _coordinator.HeartbeatSeconds = Settings.HeartbeatSeconds;
        await _activityService.ReevaluateAsync(cancellationToken);
        await _store.SaveSettingsAsync(Settings, cancellationToken);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveSessionsAndCorrectionsAsync(
        CancellationToken cancellationToken = default)
    {
        await _store.SaveSessionsAsync(Sessions, cancellationToken);
        Aggregates = RebuildAggregates();
        await _store.SaveAggregatesAsync(Aggregates, cancellationToken);
        await _store.SaveCorrectionsAsync(Corrections, cancellationToken);
    }

    /// <summary>
    /// 恢复备份前停止监听并丢弃内存中的未保存状态，避免导入完成后心跳/退出流程再次覆盖备份文件。
    /// 调用方应在导入成功后立即退出应用。
    /// </summary>
    public async Task PrepareForRestoreAsync(CancellationToken cancellationToken = default)
    {
        _coordinator.Stop();
        await _activityService.StopWithoutSavingAsync(cancellationToken);
    }

    /// <summary>导入失败时恢复本次恢复操作前的监听能力，避免应用停留在无提示的停止状态。</summary>
    public async Task ResumeAfterRestoreFailureAsync(CancellationToken cancellationToken = default)
    {
        await _activityService.ResumeAsync(cancellationToken);
        _coordinator.Start();
    }

    public void NotifyDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        _coordinator.Dispose();
        await _activityService.StopAsync();
        await _store.SaveSettingsAsync(Settings);
        await _store.SaveAppsAsync(Apps);
        Aggregates = RebuildAggregates();
        await _store.SaveAggregatesAsync(Aggregates);
        await _store.SaveCorrectionsAsync(Corrections);
        _activityService.Dispose();
    }

    private List<DailyAggregate> RebuildAggregates() =>
        Sessions
            .Where(session =>
                session.ApplicationId is not null &&
                session.EndedAtUtc is not null &&
                session.State == ActivityState.Active)
            .GroupBy(session => new
            {
                Date = DateOnly.FromDateTime(session.StartedAtUtc.ToLocalTime()),
                AppId = session.ApplicationId!.Value,
            })
            .Select(group => new DailyAggregate
            {
                Date = group.Key.Date,
                ApplicationId = group.Key.AppId,
                EffectiveSeconds = group.Sum(session => session.DurationSeconds),
                ForegroundSeconds = group.Sum(session => session.DurationSeconds),
            })
            .ToList();
}
