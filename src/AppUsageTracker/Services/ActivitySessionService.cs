using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class ActivitySessionService : IActivitySessionService
{
    private readonly IAppDataStore _store;
    private readonly IApplicationMatcher _matcher;
    private readonly ITimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppSettings _settings = new();
    private List<TrackedApp> _apps = [];
    private readonly List<ActivitySession> _sessions = [];
    private ActivitySession? _currentSession;
    private ForegroundWindowInfo? _currentWindow;
    private TrackedApp? _currentApp;
    private long _sessionStartTimestamp;
    private long _sessionBaseDuration;
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
        new(ActivityState.Stopped, null, null, null, false, false);

    public IReadOnlyList<ActivitySession> Sessions => _sessions;

    internal List<ActivitySession> SessionList => _sessions;

    public async Task StartAsync(
        AppDataState state,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _settings = state.Settings;
            _apps = state.Apps;
            _sessions.Clear();
            _sessions.AddRange(state.Sessions);
            RecoverOpenSessions();
            _started = true;
            _lastSaveAtUtc = _timeProvider.UtcNow;
            await EvaluateStateAsync(SessionEndReason.Recovered, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_started)
            {
                return;
            }

            CompleteCurrent(SessionEndReason.ApplicationExit);
            _started = false;
            Publish(ActivityState.Stopped);
            await SaveIfNeededAsync(true, cancellationToken);
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
            var appChanged = matched?.Id != _currentApp?.Id;
            _currentWindow = window;
            _currentApp = matched;
            await EvaluateStateAsync(
                appChanged ? SessionEndReason.WindowChanged : SessionEndReason.None,
                cancellationToken);
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
            if (_currentSession is not null)
            {
                UpdateCurrentDuration();
                _currentSession.LastHeartbeatAtUtc = _timeProvider.UtcNow;
                _dirty = true;
                Publish(ActivityState.Active);
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

    private async Task EvaluateStateAsync(
        SessionEndReason endReason,
        CancellationToken cancellationToken)
    {
        var targetState = DetermineState();
        var activeAppChanged =
            targetState == ActivityState.Active &&
            _currentSession?.ApplicationId != _currentApp?.Id;
        var stateChanged = _currentSession?.State != targetState;
        if (_currentSession is not null &&
            (stateChanged || activeAppChanged ||
             (endReason == SessionEndReason.WindowChanged && targetState != ActivityState.Active)))
        {
            CompleteCurrent(endReason == SessionEndReason.None
                ? SessionEndReason.WindowChanged
                : endReason);
        }

        if (targetState == ActivityState.Active && _currentSession is null && _currentApp is not null)
        {
            StartCurrent(_currentApp);
        }
        else if (_currentSession is null && ShouldRecordState(targetState))
        {
            StartStateSession(targetState);
        }

        Publish(targetState);
        await SaveIfNeededAsync(targetState != ActivityState.Active, cancellationToken);
    }

    private ActivityState DetermineState()
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

        if (_currentApp is null)
        {
            return ActivityState.Untracked;
        }

        var shouldExcludeIdle =
            _currentApp.TrackingMode == TrackingMode.Effective &&
            _settings.ExcludeIdleTime &&
            !_currentApp.IgnoreIdle;
        if (_isIdle && shouldExcludeIdle)
        {
            return ActivityState.Idle;
        }

        return ActivityState.Active;
    }

    private void StartCurrent(TrackedApp app)
    {
        var now = _timeProvider.UtcNow;
        _sessionStartTimestamp = _timeProvider.GetTimestamp();
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
            var transient = _sessions
                .Where(item =>
                    item.Id != previous.Id &&
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
                _currentSession = previous;
                _sessionBaseDuration = previous.DurationSeconds;
                _dirty = true;
                return;
            }
        }

        _sessionBaseDuration = 0;
        _currentSession = new ActivitySession
        {
            ApplicationId = app.Id,
            StartedAtUtc = now,
            LastHeartbeatAtUtc = now,
            State = ActivityState.Active,
        };
        _sessions.Add(_currentSession);
        _dirty = true;
    }

    private void StartStateSession(ActivityState state)
    {
        var now = _timeProvider.UtcNow;
        _sessionBaseDuration = 0;
        _sessionStartTimestamp = _timeProvider.GetTimestamp();
        _currentSession = new ActivitySession
        {
            StartedAtUtc = now,
            LastHeartbeatAtUtc = now,
            State = state,
        };
        _sessions.Add(_currentSession);
        _dirty = true;
    }

    private void CompleteCurrent(SessionEndReason reason)
    {
        if (_currentSession is null)
        {
            return;
        }

        UpdateCurrentDuration();
        var now = _timeProvider.UtcNow;
        _currentSession.EndedAtUtc = now;
        _currentSession.LastHeartbeatAtUtc = now;
        _currentSession.EndReason = reason;
        SplitAcrossLocalMidnights(_currentSession);
        _currentSession = null;
        _dirty = true;
    }

    private void UpdateCurrentDuration()
    {
        if (_currentSession is null)
        {
            return;
        }

        var elapsed = _timeProvider.GetElapsedSeconds(
            _sessionStartTimestamp,
            _timeProvider.GetTimestamp());
        _currentSession.DurationSeconds =
            _sessionBaseDuration + Math.Max(0, (long)Math.Floor(elapsed));
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

    private void RecoverOpenSessions()
    {
        foreach (var session in _sessions.Where(item => item.EndedAtUtc is null))
        {
            var end = session.LastHeartbeatAtUtc > session.StartedAtUtc
                ? session.LastHeartbeatAtUtc
                : session.StartedAtUtc;
            session.EndedAtUtc = end;
            session.DurationSeconds = Math.Max(0, (long)(end - session.StartedAtUtc).TotalSeconds);
            session.EndReason = SessionEndReason.Recovered;
            _dirty = true;
        }
    }

    private async Task SaveIfNeededAsync(bool force, CancellationToken cancellationToken)
    {
        if (!_dirty)
        {
            return;
        }

        var due = _timeProvider.UtcNow - _lastSaveAtUtc >=
                  TimeSpan.FromSeconds(Math.Max(5, _settings.AutoSaveSeconds));
        if (!force && !due)
        {
            return;
        }

        await _store.SaveSessionsAsync(_sessions, cancellationToken);
        _lastSaveAtUtc = _timeProvider.UtcNow;
        _dirty = false;
    }

    private void Publish(ActivityState state)
    {
        Snapshot = new TrackingSnapshot(
            state,
            _currentApp,
            _currentWindow,
            _currentSession?.Clone(),
            _isPaused,
            _isPrivateMode);
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private static bool ShouldRecordState(ActivityState state) =>
        state is ActivityState.Idle
            or ActivityState.Locked
            or ActivityState.Suspended
            or ActivityState.Paused;
}
