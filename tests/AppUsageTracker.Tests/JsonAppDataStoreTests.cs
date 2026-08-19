using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class JsonAppDataStoreTests
{
    [Fact]
    public async Task LoadAsyncReturnsDefaultsWhenFilesDoNotExist()
    {
        using var paths = new TestDataPathProvider();
        var store = new JsonAppDataStore(paths);

        var state = await store.LoadAsync();

        Assert.Equal(5, state.Settings.IdleThresholdMinutes);
        Assert.True(state.Settings.MinimizeToTrayOnClose);
        Assert.Empty(state.Apps);
        Assert.Empty(state.Sessions);
    }

    [Fact]
    public async Task SettingsAppsAndSessionsRoundTrip()
    {
        using var paths = new TestDataPathProvider();
        var store = new JsonAppDataStore(paths);
        var app = new TrackedApp
        {
            Name = "Visual Studio",
            ProcessName = "devenv.exe",
            ExecutablePath = @"C:\Tools\devenv.exe",
        };
        var session = new ActivitySession
        {
            ApplicationId = app.Id,
            StartedAtUtc = new DateTime(2026, 8, 10, 1, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 8, 10, 2, 0, 0, DateTimeKind.Utc),
            DurationSeconds = 3600,
            LastHeartbeatAtUtc = new DateTime(2026, 8, 10, 2, 0, 0, DateTimeKind.Utc),
        };

        await store.SaveSettingsAsync(new AppSettings { IdleThresholdMinutes = 8 });
        await store.SaveAppsAsync([app]);
        await store.SaveSessionsAsync([session]);
        await store.SaveAggregatesAsync(
        [
            new DailyAggregate
            {
                Date = new DateOnly(2026, 8, 10),
                ApplicationId = app.Id,
                EffectiveSeconds = 3600,
            },
        ]);

        var state = await store.LoadAsync();
        Assert.Equal(8, state.Settings.IdleThresholdMinutes);
        Assert.Equal("devenv.exe", Assert.Single(state.Apps).ProcessName);
        Assert.Equal(3600, Assert.Single(state.Sessions).DurationSeconds);
        Assert.Equal(3600, Assert.Single(state.Aggregates).EffectiveSeconds);
    }

    [Fact]
    public async Task LoadAsyncFallsBackToBackupWhenPrimaryJsonIsCorrupted()
    {
        using var paths = new TestDataPathProvider();
        var store = new JsonAppDataStore(paths);
        await store.SaveSettingsAsync(new AppSettings { IdleThresholdMinutes = 7 });
        await store.SaveSettingsAsync(new AppSettings { IdleThresholdMinutes = 11 });
        await File.WriteAllTextAsync(
            Path.Combine(paths.RootDirectory, "usage_config.json"),
            "{broken");

        var state = await store.LoadAsync();

        Assert.Equal(7, state.Settings.IdleThresholdMinutes);
    }

    [Fact]
    public async Task SaveSessionsRejectsInvalidTimeRange()
    {
        using var paths = new TestDataPathProvider();
        var store = new JsonAppDataStore(paths);
        var session = new ActivitySession
        {
            StartedAtUtc = new DateTime(2026, 8, 10, 2, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 8, 10, 1, 0, 0, DateTimeKind.Utc),
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveSessionsAsync([session]));
    }

    [Fact]
    public async Task ValidateReportsCorruptedFiles()
    {
        using var paths = new TestDataPathProvider();
        var store = new JsonAppDataStore(paths);
        await File.WriteAllTextAsync(
            Path.Combine(paths.RootDirectory, "activity_sessions.json"),
            "not-json");

        var errors = await store.ValidateAsync();

        Assert.Contains(errors, item => item.StartsWith("activity_sessions.json"));
    }

    [Fact]
    public async Task BackupCanBeCreatedAndImported()
    {
        using var sourcePaths = new TestDataPathProvider();
        using var targetPaths = new TestDataPathProvider();
        using var backupDirectory = new TestDataPathProvider();
        var source = new JsonAppDataStore(sourcePaths);
        var target = new JsonAppDataStore(targetPaths);
        await source.SaveSettingsAsync(new AppSettings { IdleThresholdMinutes = 12 });

        var backup = await source.CreateBackupAsync(backupDirectory.RootDirectory);
        await target.ImportBackupAsync(backup);
        var state = await target.LoadAsync();

        Assert.Equal(12, state.Settings.IdleThresholdMinutes);
    }
}
