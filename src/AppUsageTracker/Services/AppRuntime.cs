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
            15);
        Statistics = new StatisticsService();
    }

    public event EventHandler? DataChanged;

    public event EventHandler<TrackingSnapshot>? SnapshotChanged;

    public AppSettings Settings { get; private set; } = new();

    public List<TrackedApp> Apps { get; private set; } = [];

    public List<ActivitySession> Sessions => _activityService.SessionList;

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

        var cutoff = timeProviderLocalDate().AddDays(-Math.Max(7, Settings.MaxHistoryDays));
        state.Sessions.RemoveAll(session => session.StartedAtUtc.ToLocalTime() < cutoff);
        _idleMonitor.ThresholdMinutes = Settings.IdleThresholdMinutes;
        _coordinator.HeartbeatSeconds = Settings.HeartbeatSeconds;
        await _activityService.StartAsync(state, cancellationToken);
        _coordinator.Start();
        _initialized = true;
        DataChanged?.Invoke(this, EventArgs.Empty);

        DateTime timeProviderLocalDate() => TimeProvider.UtcNow.ToLocalTime().Date;
    }

    public async Task SaveAppsAsync(CancellationToken cancellationToken = default)
    {
        // 新增的软件可能还带着默认色，保存前统一补一次槽位。
        AppColorPalette.AssignMissing(Apps);
        await _store.SaveAppsAsync(Apps, cancellationToken);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        _idleMonitor.ThresholdMinutes = Settings.IdleThresholdMinutes;
        _coordinator.HeartbeatSeconds = Settings.HeartbeatSeconds;
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
