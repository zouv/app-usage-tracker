using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface IAppDataStore
{
    Task<AppDataState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task SaveAppsAsync(IReadOnlyCollection<TrackedApp> apps, CancellationToken cancellationToken = default);

    Task SaveSessionsAsync(IReadOnlyCollection<ActivitySession> sessions, CancellationToken cancellationToken = default);

    Task SaveAggregatesAsync(
        IReadOnlyCollection<DailyAggregate> aggregates,
        CancellationToken cancellationToken = default);

    Task SaveCorrectionsAsync(
        IReadOnlyCollection<ManualCorrection> corrections,
        CancellationToken cancellationToken = default);

    Task<string> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default);

    Task ImportBackupAsync(string backupFile, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ValidateAsync(CancellationToken cancellationToken = default);
}
