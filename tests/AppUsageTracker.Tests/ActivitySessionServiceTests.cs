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
}
