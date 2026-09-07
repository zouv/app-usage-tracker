using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class ActivitySessionServiceTests
{
    [Fact]
    public async Task WindowSwitchCompletesOldSessionAndStartsNewSession()
    {
        var start = new DateTime(2026, 8, 10, 1, 0, 0, DateTimeKind.Utc);
        var time = new FakeTimeProvider(start);
        var store = new MemoryAppDataStore();
        var first = new TrackedApp { Name = "First", ProcessName = "first.exe" };
        var second = new TrackedApp { Name = "Second", ProcessName = "second.exe" };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState
        {
            Apps = [first, second],
            Settings = new AppSettings { AutoSaveSeconds = 60 },
        });

        await service.HandleForegroundWindowAsync(Window("first.exe", 1));
        time.Advance(TimeSpan.FromSeconds(12));
        await service.HandleForegroundWindowAsync(Window("second.exe", 2));

        Assert.Equal(ActivityState.Active, service.Snapshot.State);
        Assert.Equal(second.Id, service.Snapshot.CurrentApp?.Id);
        Assert.Equal(2, service.Sessions.Count);
        Assert.Equal(12, service.Sessions[0].DurationSeconds);
        Assert.Equal(SessionEndReason.WindowChanged, service.Sessions[0].EndReason);
    }

    [Fact]
    public async Task IdleLockedPausedAndPrivateStatesDoNotCreateActiveSessions()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var store = new MemoryAppDataStore();
        var app = new TrackedApp { Name = "Editor", ProcessName = "editor.exe" };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState { Apps = [app] });
        await service.HandleForegroundWindowAsync(Window("editor.exe", 1));

        time.Advance(TimeSpan.FromSeconds(5));
        await service.SetIdleAsync(true);
        Assert.Equal(ActivityState.Idle, service.Snapshot.State);
        Assert.Equal(ActivityState.Idle, service.Snapshot.CurrentSession?.State);

        await service.SetIdleAsync(false);
        await service.SetSystemStateAsync(SystemSessionState.Locked);
        Assert.Equal(ActivityState.Locked, service.Snapshot.State);

        await service.SetSystemStateAsync(SystemSessionState.Available);
        await service.SetPausedAsync(true);
        Assert.Equal(ActivityState.Paused, service.Snapshot.State);

        await service.SetPausedAsync(false);
        await service.SetPrivateModeAsync(true);
        Assert.Equal(ActivityState.Private, service.Snapshot.State);
        Assert.Null(service.Snapshot.CurrentSession);
    }

    [Fact]
    public async Task OpenSessionIsRecoveredAtLastHeartbeat()
    {
        var start = new DateTime(2026, 8, 10, 1, 0, 0, DateTimeKind.Utc);
        var open = new ActivitySession
        {
            StartedAtUtc = start,
            LastHeartbeatAtUtc = start.AddSeconds(30),
        };
        using var service = new ActivitySessionService(
            new MemoryAppDataStore(),
            new ApplicationMatcher(),
            new FakeTimeProvider(start.AddMinutes(5)));

        await service.StartAsync(new AppDataState { Sessions = [open] });

        Assert.Equal(start.AddSeconds(30), open.EndedAtUtc);
        Assert.Equal(30, open.DurationSeconds);
        Assert.Equal(SessionEndReason.Recovered, open.EndReason);
    }

    [Fact]
    public async Task SwitchingBetweenWindowsOfSameAppKeepsSingleSession()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var app = new TrackedApp { Name = "Editor", ProcessName = "editor.exe" };
        using var service = new ActivitySessionService(
            new MemoryAppDataStore(),
            new ApplicationMatcher(),
            time);
        await service.StartAsync(new AppDataState { Apps = [app] });
        await service.HandleForegroundWindowAsync(Window("editor.exe", 1));
        time.Advance(TimeSpan.FromSeconds(4));

        await service.HandleForegroundWindowAsync(Window("editor.exe", 2));

        Assert.Single(service.Sessions);
        Assert.Null(service.Sessions[0].EndedAtUtc);
    }

    [Fact]
    public async Task BriefUntrackedSwitchIsMergedIntoPreviousSession()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var app = new TrackedApp { Name = "Editor", ProcessName = "editor.exe" };
        using var service = new ActivitySessionService(
            new MemoryAppDataStore(),
            new ApplicationMatcher(),
            time);
        await service.StartAsync(new AppDataState
        {
            Apps = [app],
            Settings = new AppSettings { ShortSwitchSeconds = 5 },
        });
        await service.HandleForegroundWindowAsync(Window("editor.exe", 1));
        time.Advance(TimeSpan.FromSeconds(10));
        await service.HandleForegroundWindowAsync(Window("notification.exe", 2));
        time.Advance(TimeSpan.FromSeconds(3));

        await service.HandleForegroundWindowAsync(Window("editor.exe", 3));

        Assert.Single(service.Sessions);
        Assert.Null(service.Sessions[0].EndedAtUtc);
        Assert.Equal(10, service.Sessions[0].DurationSeconds);
    }

    [Fact]
    public async Task SessionCrossingLocalMidnightIsSplit()
    {
        var localStart = DateTime.SpecifyKind(
            new DateTime(2026, 8, 10, 23, 59, 30),
            DateTimeKind.Local);
        var time = new FakeTimeProvider(localStart.ToUniversalTime());
        var app = new TrackedApp { Name = "Game", ProcessName = "game.exe" };
        using var service = new ActivitySessionService(
            new MemoryAppDataStore(),
            new ApplicationMatcher(),
            time);
        await service.StartAsync(new AppDataState { Apps = [app] });
        await service.HandleForegroundWindowAsync(Window("game.exe", 1));

        time.Advance(TimeSpan.FromSeconds(90));
        await service.SetPausedAsync(true);

        var activeSegments = service.Sessions.Where(item => item.State == ActivityState.Active).ToList();
        Assert.Equal(2, activeSegments.Count);
        Assert.Equal(90, activeSegments.Sum(item => item.DurationSeconds));
        Assert.Equal(SessionEndReason.Midnight, activeSegments[0].EndReason);
    }

    private static ForegroundWindowInfo Window(string processName, nint handle) =>
        new(handle, (int)handle, processName, string.Empty, processName);

    [Fact]
    public async Task RunningModeAppAccumulatesWhileProcessRunsWithoutForeground()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var store = new MemoryAppDataStore();
        var app = new TrackedApp
        {
            Name = "Daemon",
            ProcessName = "daemon.exe",
            TrackingMode = TrackingMode.Running,
        };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState
        {
            Apps = [app],
            Settings = new AppSettings { AutoSaveSeconds = 60 },
        });

        // 前台是未配置软件，运行模式软件只在后台进程里。
        await service.HandleForegroundWindowAsync(Window("other.exe", 1));
        await service.HandleRunningProcessesAsync([Process("daemon.exe")]);
        Assert.Equal(ActivityState.Active, service.Snapshot.State);
        Assert.Equal(app.Id, service.Snapshot.CurrentApp?.Id);

        time.Advance(TimeSpan.FromSeconds(30));
        await service.HeartbeatAsync();

        // 进程退出后会话结束并保留时长。
        await service.HandleRunningProcessesAsync([]);

        var session = Assert.Single(
            service.Sessions.Where(item => item.ApplicationId == app.Id));
        Assert.NotNull(session.EndedAtUtc);
        Assert.Equal(30, session.DurationSeconds);
        Assert.Equal(ActivityState.Untracked, service.Snapshot.State);
    }

    [Fact]
    public async Task ForegroundAndRunningAppsAccumulateInParallel()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var store = new MemoryAppDataStore();
        var runningApp = new TrackedApp
        {
            Name = "Daemon",
            ProcessName = "daemon.exe",
            TrackingMode = TrackingMode.Running,
        };
        var foregroundApp = new TrackedApp
        {
            Name = "Editor",
            ProcessName = "editor.exe",
            TrackingMode = TrackingMode.Effective,
        };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState { Apps = [runningApp, foregroundApp] });

        await service.HandleRunningProcessesAsync([Process("daemon.exe")]);
        await service.HandleForegroundWindowAsync(Window("editor.exe", 1));

        // 前台软件与后台运行软件同时各自累计。
        Assert.Equal(2, service.Snapshot.ActiveApps.Count);
        Assert.Contains(service.Snapshot.ActiveApps, info => info.App.Id == runningApp.Id);
        Assert.Contains(service.Snapshot.ActiveApps, info => info.App.Id == foregroundApp.Id);

        time.Advance(TimeSpan.FromSeconds(30));
        await service.HeartbeatAsync();

        Assert.Equal(2, service.Sessions.Count(item => item.EndedAtUtc is null));
        // 主显示项仍是前台命中的软件。
        Assert.Equal(foregroundApp.Id, service.Snapshot.CurrentApp?.Id);
    }

    [Fact]
    public async Task MultipleRunningModeAppsAccumulateTogether()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var store = new MemoryAppDataStore();
        var first = new TrackedApp
        {
            Name = "DaemonA",
            ProcessName = "daemon-a.exe",
            TrackingMode = TrackingMode.Running,
        };
        var second = new TrackedApp
        {
            Name = "DaemonB",
            ProcessName = "daemon-b.exe",
            TrackingMode = TrackingMode.Running,
        };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState { Apps = [first, second] });
        await service.HandleForegroundWindowAsync(Window("other.exe", 1));

        await service.HandleRunningProcessesAsync([Process("daemon-a.exe"), Process("daemon-b.exe")]);

        Assert.Equal(2, service.Snapshot.ActiveApps.Count);
        time.Advance(TimeSpan.FromSeconds(20));
        await service.HeartbeatAsync();

        // 其中一个进程退出，只结束对应软件会话，另一个继续。
        await service.HandleRunningProcessesAsync([Process("daemon-b.exe")]);

        var firstSession = service.Sessions.Single(item => item.ApplicationId == first.Id);
        var secondSession = service.Sessions.Single(item => item.ApplicationId == second.Id);
        Assert.NotNull(firstSession.EndedAtUtc);
        Assert.Equal(20, firstSession.DurationSeconds);
        Assert.Null(secondSession.EndedAtUtc);
        Assert.Single(service.Snapshot.ActiveApps);
        Assert.Equal(second.Id, service.Snapshot.ActiveApps[0].App.Id);
    }

    [Fact]
    public async Task SameAppForegroundAndRunningKeepsSingleSession()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var store = new MemoryAppDataStore();
        // 同一软件既是前台命中、又配置为运行模式：只应产生一条会话。
        var app = new TrackedApp
        {
            Name = "Game",
            ProcessName = "game.exe",
            TrackingMode = TrackingMode.Running,
        };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState { Apps = [app] });

        await service.HandleRunningProcessesAsync([Process("game.exe")]);
        await service.HandleForegroundWindowAsync(Window("game.exe", 1));

        Assert.Single(service.Snapshot.ActiveApps);
        Assert.Single(service.Sessions, item => item.ApplicationId == app.Id && item.EndedAtUtc is null);
    }

    [Fact]
    public async Task LockingEndsAllActiveAppSessions()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var store = new MemoryAppDataStore();
        var runningApp = new TrackedApp
        {
            Name = "Daemon",
            ProcessName = "daemon.exe",
            TrackingMode = TrackingMode.Running,
        };
        var foregroundApp = new TrackedApp
        {
            Name = "Editor",
            ProcessName = "editor.exe",
            TrackingMode = TrackingMode.Effective,
        };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState { Apps = [runningApp, foregroundApp] });
        await service.HandleRunningProcessesAsync([Process("daemon.exe")]);
        await service.HandleForegroundWindowAsync(Window("editor.exe", 1));
        time.Advance(TimeSpan.FromSeconds(10));

        await service.SetSystemStateAsync(SystemSessionState.Locked);

        Assert.Empty(service.Snapshot.ActiveApps);
        Assert.Equal(ActivityState.Locked, service.Snapshot.State);
        Assert.All(
            service.Sessions.Where(item => item.ApplicationId is not null),
            item => Assert.NotNull(item.EndedAtUtc));
    }

    [Fact]
    public async Task IdleEndsOnlyEffectiveAppsNotRunningApps()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        var store = new MemoryAppDataStore();
        var runningApp = new TrackedApp
        {
            Name = "Daemon",
            ProcessName = "daemon.exe",
            TrackingMode = TrackingMode.Running,
        };
        var effectiveApp = new TrackedApp
        {
            Name = "Editor",
            ProcessName = "editor.exe",
            TrackingMode = TrackingMode.Effective,
        };
        using var service = new ActivitySessionService(store, new ApplicationMatcher(), time);
        await service.StartAsync(new AppDataState { Apps = [runningApp, effectiveApp] });
        await service.HandleRunningProcessesAsync([Process("daemon.exe")]);
        await service.HandleForegroundWindowAsync(Window("editor.exe", 1));
        time.Advance(TimeSpan.FromSeconds(10));

        await service.SetIdleAsync(true);

        // 有效模式软件被空闲结束，运行模式软件继续累计。
        var effectiveSession = service.Sessions.Single(item => item.ApplicationId == effectiveApp.Id);
        var runningSession = service.Sessions.Single(item => item.ApplicationId == runningApp.Id);
        Assert.NotNull(effectiveSession.EndedAtUtc);
        Assert.Null(runningSession.EndedAtUtc);
        Assert.Single(service.Snapshot.ActiveApps);
        Assert.Equal(runningApp.Id, service.Snapshot.ActiveApps[0].App.Id);
    }

    private static RunningProcessInfo Process(string processName) =>
        new(1, processName, string.Empty, string.Empty);
}
