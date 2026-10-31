using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class ActivitySessionService : IActivitySessionService
{
    private readonly IAppDataStore _store;
    private readonly IApplicationMatcher _matcher;
    private readonly ITimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppSettings _settings = new();
    private IReadOnlyList<TrackedApp> _apps = [];

    /// <summary>所有原始会话。只在 _sessionsLock 内读写，对外只暴露深拷贝快照。</summary>
    private readonly object _sessionsLock = new();
    private readonly List<ActivitySession> _sessions = [];
    private DateOnly _lastPruneDate;

    /// <summary>正在累计的软件会话，按软件 Id 键控；一个软件同一时刻只存在一条。</summary>
    private readonly Dictionary<Guid, ActivitySession> _appSessions = [];

    /// <summary>全局状态会话（空闲/锁屏/休眠/暂停/隐私），ApplicationId 为空，最多一条。</summary>
    private ActivitySession? _stateSession;
    private ForegroundWindowInfo? _currentWindow;
    private TrackedApp? _currentApp;
    private IReadOnlyList<TrackedApp> _runningApps = [];

    /// <summary>每个软件会话的单调计时起点与已并入的基础时长。</summary>
    private readonly Dictionary<Guid, SessionClock> _appClocks = [];
    private SessionClock _stateClock;
    private DateTime _lastSaveAtUtc = DateTime.MinValue;
    private SystemSessionState _systemState = SystemSessionState.Available;
    private bool _started;
    private bool _isIdle;
    private bool _isPaused;
    private bool _isPrivateMode;
    private bool _dirty;
    private bool _disposed;

    public ActivitySessionService(
        IAppDataStore store,
        IApplicationMatcher matcher,
        ITimeProvider timeProvider)
    {
        _store = store;
        _matcher = matcher;
        _timeProvider = timeProvider;
    }

    public event EventHandler<TrackingSnapshot>? SnapshotChanged;

    public TrackingSnapshot Snapshot { get; private set; } =
        new(ActivityState.Stopped, null, null, null, false, false, []);

    /// <summary>深拷贝快照，供 UI 与统计在后台线程安全读取。</summary>
    public IReadOnlyList<ActivitySession> Sessions
    {
        get
        {
            lock (_sessionsLock)
            {
                return _sessions.Select(session => session.Clone()).ToList();
            }
        }
    }

    internal List<ActivitySession> SessionList => _sessions;

    /// <summary>只有启用了「运行」模式且未被全局阻断时才需要扫描进程。</summary>
    public bool ShouldScanRunningProcesses =>
        _started &&
        BlockingState() is null &&
        _apps.Any(app => app.Enabled && app.TrackingMode == TrackingMode.Running);

    public IReadOnlyList<TrackedApp> AppsSnapshot => _apps;

    /// <summary>由运行模式软件的进程名、关联进程名、路径和进程规则推导出的候选进程名。</summary>
    public IReadOnlyCollection<string> RunningProcessScanNames
    {
        get
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in _apps)
            {
                if (!app.Enabled || app.TrackingMode != TrackingMode.Running)
                {
                    continue;
                }

                AddProcessName(names, app.ProcessName);
                AddProcessName(names, app.ExecutablePath);
                foreach (var related in app.RelatedProcessNames)
                {
                    AddProcessName(names, related);
                }

                foreach (var rule in app.MatchRules)
                {
                    if (rule.Type is MatchRuleType.ProcessName or MatchRuleType.ExecutablePath)
                    {
                        AddProcessName(names, rule.Pattern);
                    }
                }
            }

            return names;
        }
    }

    public async Task StartAsync(
        AppDataState state,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _settings = state.Settings;
            _apps = state.Apps.Select(app => app.Clone()).ToList();
            lock (_sessionsLock)
            {
                _sessions.Clear();
                _sessions.AddRange(state.Sessions);
            }

            RecoverOpenSessions();
            PruneExpiredSessions();
            _started = true;
            _lastSaveAtUtc = _timeProvider.UtcNow;
            await EvaluateStateAsync(SessionEndReason.Recovered, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        StopCoreAsync(save: true, cancellationToken);

    public Task StopWithoutSavingAsync(CancellationToken cancellationToken = default) =>
        StopCoreAsync(save: false, cancellationToken);

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _lastSaveAtUtc = _timeProvider.UtcNow;
            await EvaluateStateAsync(SessionEndReason.Recovered, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCoreAsync(bool save, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_started)
            {
                return;
            }

            CompleteAllSessions(SessionEndReason.ApplicationExit);
            _started = false;
            Publish();
            if (save)
            {
                await SaveIfNeededAsync(true, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandleForegroundWindowAsync(
        ForegroundWindowInfo? window,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var matched = window is null ? null : _matcher.Match(window, _apps);
            if (Equals(_currentWindow, window) && _currentApp?.Id == matched?.Id)
            {
                return;
            }

            _currentWindow = window;
            _currentApp = matched;
            await EvaluateStateAsync(SessionEndReason.None, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandleRunningProcessesAsync(
        IReadOnlyCollection<RunningProcessInfo> processes,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var matched = _matcher.MatchRunningProcess(processes, _apps);
            if (_runningApps.Select(app => app.Id).SequenceEqual(matched.Select(app => app.Id)))
            {
                return;
            }

            _runningApps = matched;
            await EvaluateStateAsync(SessionEndReason.None, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAppsAsync(
        IReadOnlyList<TrackedApp> apps,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _apps = apps.Select(app => app.Clone()).ToList();
            RefreshCurrentMatch();
            await EvaluateStateAsync(SessionEndReason.None, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> EditSessionsAsync<T>(
        Func<List<ActivitySession>, T> edit,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            T result;
            lock (_sessionsLock)
            {
                result = edit(_sessions);
            }

            _dirty = true;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CompleteAllSessions(SessionEndReason.Manual);
            lock (_sessionsLock)
            {
                _sessions.Clear();
            }

            _dirty = true;
            Publish();
            await SaveIfNeededAsync(true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReevaluateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EvaluateStateAsync(SessionEndReason.None, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetIdleAsync(bool isIdle, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_isIdle == isIdle)
            {
                return;
            }

            _isIdle = isIdle;
            await EvaluateStateAsync(SessionEndReason.Idle, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetSystemStateAsync(
        SystemSessionState state,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_systemState == state)
            {
                return;
            }

            _systemState = state;
            var reason = state switch
            {
                SystemSessionState.Locked => SessionEndReason.Locked,
                SystemSessionState.Suspended => SessionEndReason.Suspended,
                _ => SessionEndReason.None,
            };
            await EvaluateStateAsync(reason, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_isPaused == paused)
            {
                return;
            }

            _isPaused = paused;
            await EvaluateStateAsync(SessionEndReason.Paused, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetPrivateModeAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_isPrivateMode == enabled)
            {
                return;
            }

            _isPrivateMode = enabled;
            await EvaluateStateAsync(SessionEndReason.PrivateMode, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PruneExpiredSessions();

            // 软件配置可能在运行中被编辑：心跳时重新匹配当前窗口，
            // 确保停用/删除/修改匹配规则后能及时结束旧会话。
            var previousAppId = _currentApp?.Id;
            RefreshCurrentMatch();
            if (previousAppId != _currentApp?.Id)
            {
                await EvaluateStateAsync(SessionEndReason.None, cancellationToken);
            }

            var now = _timeProvider.UtcNow;
            foreach (var (appId, session) in _appSessions)
            {
                UpdateSessionDuration(session, _appClocks[appId]);
                session.LastHeartbeatAtUtc = now;
            }

            if (_stateSession is not null)
            {
                UpdateSessionDuration(_stateSession, _stateClock);
                _stateSession.LastHeartbeatAtUtc = now;
            }

            if (_appSessions.Count > 0 || _stateSession is not null)
            {
                _dirty = true;
                Publish();
            }

            await SaveIfNeededAsync(false, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    /// <summary>全局阻断态（隐私/暂停/锁屏/休眠）优先；否则各软件并行累计，前台命中与运行模式软件共存。</summary>
    private async Task EvaluateStateAsync(
        SessionEndReason endReason,
        CancellationToken cancellationToken)
    {
        var blocking = BlockingState();
        if (blocking is not null)
        {
            CompleteAllSessions(endReason == SessionEndReason.None
                ? ReasonForState(blocking.Value)
                : endReason);
            StartStateSessionIfNeeded(blocking.Value);
        }
        else
        {
            CompleteStateSession();
            var targets = ResolveActiveApps();
            var targetIds = targets.Select(app => app.Id).ToHashSet();

            // 结束不再活跃（或被空闲排除）的软件会话。
            foreach (var appId in _appSessions.Keys.ToList())
            {
                if (!targetIds.Contains(appId))
                {
                    CompleteAppSession(appId, endReason == SessionEndReason.None
                        ? SessionEndReason.WindowChanged
                        : endReason);
                }
            }

            // 为每个目标软件开新会话（已存在的继续累计）。
            foreach (var app in targets)
            {
                if (!_appSessions.ContainsKey(app.Id))
                {
                    StartAppSession(app);
                }
            }

            // 没有任何软件仍活跃且处于空闲时，记录一条全局空闲会话。
            if (targets.Count == 0 && _isIdle)
            {
                StartStateSessionIfNeeded(ActivityState.Idle);
            }
        }

        Publish();
        await SaveIfNeededAsync(
            _appSessions.Count == 0,
            cancellationToken,
            allowForceThrottle: true);
    }

    /// <summary>当前应累计的软件集合：前台命中优先，叠加与前台不同 Id 的运行模式软件；同一软件只出现一次。</summary>
    private List<TrackedApp> ResolveActiveApps()
    {
        var result = new List<TrackedApp>();
        var seen = new HashSet<Guid>();

        // 配置可能刚被编辑或删除：只使用当前配置列表里仍启用、仍存在的软件对象。
        var foreground = _currentApp is null
            ? null
            : _apps.FirstOrDefault(app => app.Id == _currentApp.Id);
        if (foreground is { Enabled: true } && !IsExcludedByIdle(foreground))
        {
            result.Add(foreground);
            seen.Add(foreground.Id);
        }

        foreach (var running in _runningApps)
        {
            var app = _apps.FirstOrDefault(item => item.Id == running.Id);
            if (app is not { Enabled: true } ||
                app.TrackingMode != TrackingMode.Running ||
                !seen.Add(app.Id) ||
                IsExcludedByIdle(app))
            {
                continue;
            }

            result.Add(app);
        }

        return result;
    }

    /// <summary>仅「有效」模式且开启排除空闲、未忽略空闲的软件受空闲影响。</summary>
    private bool IsExcludedByIdle(TrackedApp app) =>
        _isIdle &&
        app.TrackingMode == TrackingMode.Effective &&
        _settings.ExcludeIdleTime &&
        !app.IgnoreIdle;

    private ActivityState? BlockingState()
    {
        if (!_started)
        {
            return ActivityState.Stopped;
        }

        if (_isPrivateMode)
        {
            return ActivityState.Private;
        }

        if (_isPaused)
        {
            return ActivityState.Paused;
        }

        if (_systemState == SystemSessionState.Locked)
        {
            return ActivityState.Locked;
        }

        if (_systemState == SystemSessionState.Suspended)
        {
            return ActivityState.Suspended;
        }

        return null;
    }

    private void StartAppSession(TrackedApp app)
    {
        lock (_sessionsLock)
        {
            var now = _timeProvider.UtcNow;
            var timestamp = _timeProvider.GetTimestamp();
            var mergeThreshold = TimeSpan.FromSeconds(Math.Max(0, _settings.ShortSwitchSeconds));
            var previous = _sessions
                .Where(item =>
                    item.ApplicationId == app.Id &&
                    item.EndedAtUtc is not null &&
                    item.EndReason == SessionEndReason.WindowChanged)
                .OrderByDescending(item => item.EndedAtUtc)
                .FirstOrDefault();
            if (previous?.EndedAtUtc is { } previousEnd &&
                now - previousEnd <= mergeThreshold)
            {
                // 只清理短切换产生的已结束软件会话；空闲/锁屏等全局状态会话与手工记录不能被吞掉。
                var transient = _sessions
                    .Where(item =>
                        item.Id != previous.Id &&
                        item.ApplicationId is not null &&
                        item.State == ActivityState.Active &&
                        !item.IsManual &&
                        item.StartedAtUtc >= previousEnd &&
                        item.EndedAtUtc is not null)
                    .ToList();
                if (transient.Sum(item => item.DurationSeconds) <= _settings.ShortSwitchSeconds)
                {
                    foreach (var item in transient)
                    {
                        _sessions.Remove(item);
                    }

                    previous.EndedAtUtc = null;
                    previous.EndReason = SessionEndReason.None;
                    previous.LastHeartbeatAtUtc = now;
                    _appSessions[app.Id] = previous;
                    _appClocks[app.Id] = new SessionClock(timestamp, previous.DurationSeconds);
                    _dirty = true;
                    return;
                }
            }

            var session = new ActivitySession
            {
                ApplicationId = app.Id,
                StartedAtUtc = now,
                LastHeartbeatAtUtc = now,
                State = ActivityState.Active,
            };
            _sessions.Add(session);
            _appSessions[app.Id] = session;
            _appClocks[app.Id] = new SessionClock(timestamp, 0);
            _dirty = true;
        }
    }

    private void StartStateSessionIfNeeded(ActivityState state)
    {
        if (!ShouldRecordState(state))
        {
            return;
        }

        lock (_sessionsLock)
        {
            if (_stateSession?.State == state)
            {
                return;
            }

            CompleteStateSession();
            _stateClock = new SessionClock(_timeProvider.GetTimestamp(), 0);
            _stateSession = new ActivitySession
            {
                StartedAtUtc = _timeProvider.UtcNow,
                LastHeartbeatAtUtc = _timeProvider.UtcNow,
                State = state,
            };
            _sessions.Add(_stateSession);
            _dirty = true;
        }
    }

    private void CompleteAppSession(Guid appId, SessionEndReason reason)
    {
        if (!_appSessions.TryGetValue(appId, out var session))
        {
            return;
        }

        lock (_sessionsLock)
        {
            UpdateSessionDuration(session, _appClocks[appId]);
            var now = _timeProvider.UtcNow;
            session.EndedAtUtc = now;
            session.LastHeartbeatAtUtc = now;
            session.EndReason = reason;
            _appSessions.Remove(appId);
            _appClocks.Remove(appId);
            SplitAcrossLocalMidnights(session);
            _dirty = true;
        }
    }

    private void CompleteStateSession()
    {
        if (_stateSession is null)
        {
            return;
        }

        lock (_sessionsLock)
        {
            UpdateSessionDuration(_stateSession, _stateClock);
            var now = _timeProvider.UtcNow;
            var session = _stateSession;
            session.EndedAtUtc = now;
            session.LastHeartbeatAtUtc = now;
            _stateSession = null;
            SplitAcrossLocalMidnights(session);
            _dirty = true;
        }
    }

    private void CompleteAllSessions(SessionEndReason reason)
    {
        foreach (var appId in _appSessions.Keys.ToList())
        {
            CompleteAppSession(appId, reason);
        }

        CompleteStateSession();
    }

    private void UpdateSessionDuration(ActivitySession session, SessionClock clock)
    {
        var elapsed = _timeProvider.GetElapsedSeconds(
            clock.StartTimestamp,
            _timeProvider.GetTimestamp());
        session.DurationSeconds =
            clock.BaseDuration + Math.Max(0, (long)Math.Floor(elapsed));
    }

    private void SplitAcrossLocalMidnights(ActivitySession session)
    {
        if (session.EndedAtUtc is not { } endedAtUtc)
        {
            return;
        }

        var startLocal = session.StartedAtUtc.ToLocalTime();
        var endLocal = endedAtUtc.ToLocalTime();
        if (DateOnly.FromDateTime(startLocal) == DateOnly.FromDateTime(endLocal))
        {
            return;
        }

        lock (_sessionsLock)
        {
            _sessions.Remove(session);
            var cursorUtc = session.StartedAtUtc;
            var remainingSeconds = session.DurationSeconds;
            while (cursorUtc < endedAtUtc)
            {
                var cursorLocal = cursorUtc.ToLocalTime();
                var nextLocalMidnight = cursorLocal.Date.AddDays(1);
                var nextBoundaryUtc = nextLocalMidnight.ToUniversalTime();
                var segmentEnd = nextBoundaryUtc < endedAtUtc ? nextBoundaryUtc : endedAtUtc;
                var segmentWallSeconds = Math.Max(0, (long)(segmentEnd - cursorUtc).TotalSeconds);
                var segmentSeconds = segmentEnd == endedAtUtc
                    ? remainingSeconds
                    : Math.Min(remainingSeconds, segmentWallSeconds);

                var segment = session.Clone();
                segment.Id = Guid.NewGuid();
                segment.StartedAtUtc = cursorUtc;
                segment.EndedAtUtc = segmentEnd;
                segment.DurationSeconds = segmentSeconds;
                if (segmentEnd < endedAtUtc)
                {
                    segment.EndReason = SessionEndReason.Midnight;
                }

                _sessions.Add(segment);
                remainingSeconds -= segmentSeconds;
                cursorUtc = segmentEnd;
            }
        }
    }

    private void RecoverOpenSessions()
    {
        List<ActivitySession> open;
        lock (_sessionsLock)
        {
            open = _sessions.Where(item => item.EndedAtUtc is null).ToList();
        }

        foreach (var session in open)
        {
            var end = session.LastHeartbeatAtUtc > session.StartedAtUtc
                ? session.LastHeartbeatAtUtc
                : session.StartedAtUtc;
            session.EndedAtUtc = end;
            session.DurationSeconds = Math.Max(0, (long)(end - session.StartedAtUtc).TotalSeconds);
            session.EndReason = SessionEndReason.Recovered;
            SplitAcrossLocalMidnights(session);
            _dirty = true;
        }
    }

    private async Task SaveIfNeededAsync(
        bool force,
        CancellationToken cancellationToken,
        bool allowForceThrottle = false)
    {
        if (!_dirty)
        {
            return;
        }

        var due = _timeProvider.UtcNow - _lastSaveAtUtc >=
                  TimeSpan.FromSeconds(Math.Max(5, _settings.AutoSaveSeconds));
        if (allowForceThrottle && force && !due &&
            _timeProvider.UtcNow - _lastSaveAtUtc < TimeSpan.FromSeconds(15))
        {
            // 频繁在已配置/未配置软件之间切换时，不每次都重写整份历史；
            // 15 秒兜底仍能保证异常退出时最多丢失一小段记录。显式退出/清空不受此限制。
            force = false;
        }

        if (!force && !due)
        {
            return;
        }

        var snapshot = Sessions;
        await _store.SaveSessionsAsync(snapshot, cancellationToken);
        _lastSaveAtUtc = _timeProvider.UtcNow;
        _dirty = false;
    }

    /// <summary>按当前配置重新匹配前台窗口；配置被删除或停用后返回 null。</summary>
    private void RefreshCurrentMatch()
    {
        _currentApp = _currentWindow is null ? null : _matcher.Match(_currentWindow, _apps);
    }

    /// <summary>按 MaxHistoryDays 清理过期历史；每天最多扫描一次，避免长时间运行后无限增长。</summary>
    private void PruneExpiredSessions()
    {
        var localToday = DateOnly.FromDateTime(_timeProvider.UtcNow.ToLocalTime());
        if (localToday == _lastPruneDate)
        {
            return;
        }

        _lastPruneDate = localToday;
        var cutoff = localToday.AddDays(-Math.Max(7, _settings.MaxHistoryDays));
        var removed = 0;
        lock (_sessionsLock)
        {
            removed = _sessions.RemoveAll(session =>
                session.EndedAtUtc is not null &&
                DateOnly.FromDateTime(session.StartedAtUtc.ToLocalTime()) < cutoff);
        }

        if (removed > 0)
        {
            _dirty = true;
        }
    }

    private void Publish()
    {
        var activeApps = _appSessions
            .Select(pair => BuildActiveAppInfo(pair.Key, pair.Value))
            .Where(info => info is not null)
            .Select(info => info!)
            .OrderByDescending(info => info.App.Id == _currentApp?.Id)
            .ThenBy(info => info.App.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var primary = activeApps.FirstOrDefault();
        var state = ComputeSnapshotState(activeApps.Count);
        // 主显示会话：优先进行中的软件会话；无软件活跃时回退到全局状态会话（空闲/锁屏等）。
        var primarySession = primary?.Session ?? _stateSession;
        Snapshot = new TrackingSnapshot(
            state,
            primary?.App,
            _currentWindow,
            primarySession?.Clone(),
            _isPaused,
            _isPrivateMode,
            activeApps);
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private ActiveAppInfo? BuildActiveAppInfo(Guid appId, ActivitySession session)
    {
        var app = _apps.FirstOrDefault(item => item.Id == appId);
        return app is null
            ? null
            : new ActiveAppInfo(app, session.Clone(), app.TrackingMode);
    }

    /// <summary>有任一软件活跃即 Active；否则按全局阻断态或空闲/未跟踪给出聚合状态。</summary>
    private ActivityState ComputeSnapshotState(int activeCount)
    {
        if (activeCount > 0)
        {
            return ActivityState.Active;
        }

        if (BlockingState() is { } blocking)
        {
            return blocking;
        }

        if (!_started)
        {
            return ActivityState.Stopped;
        }

        return _isIdle ? ActivityState.Idle : ActivityState.Untracked;
    }

    private static SessionEndReason ReasonForState(ActivityState state) => state switch
    {
        ActivityState.Locked => SessionEndReason.Locked,
        ActivityState.Suspended => SessionEndReason.Suspended,
        ActivityState.Paused => SessionEndReason.Paused,
        ActivityState.Private => SessionEndReason.PrivateMode,
        _ => SessionEndReason.None,
    };

    private static bool ShouldRecordState(ActivityState state) =>
        state is ActivityState.Idle
            or ActivityState.Locked
            or ActivityState.Suspended
            or ActivityState.Paused;

    private static void AddProcessName(ISet<string> names, string value)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(value?.Trim());
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }

    /// <summary>单个会话的单调计时：起点时间戳 + 已并入的基础时长（短切换合并时保留旧时长）。</summary>
    private readonly struct SessionClock(long startTimestamp, long baseDuration)
    {
        public long StartTimestamp { get; } = startTimestamp;

        public long BaseDuration { get; } = baseDuration;
    }
}
