using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

internal sealed class MemoryAppDataStore : IAppDataStore
{
    public List<ActivitySession> SavedSessions { get; private set; } = [];

    public Task<AppDataState> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDataState());

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveAppsAsync(
        IReadOnlyCollection<TrackedApp> apps,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveSessionsAsync(
        IReadOnlyCollection<ActivitySession> sessions,
        CancellationToken cancellationToken = default)
    {
        SavedSessions = sessions.Select(session => session.Clone()).ToList();
        return Task.CompletedTask;
    }

    public Task SaveCorrectionsAsync(
        IReadOnlyCollection<ManualCorrection> corrections,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveAggregatesAsync(
        IReadOnlyCollection<DailyAggregate> aggregates,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<string> CreateBackupAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);

    public Task ImportBackupAsync(
        string backupFile,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<string>> ValidateAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
