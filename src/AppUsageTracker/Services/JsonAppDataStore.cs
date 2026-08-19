using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class JsonAppDataStore : IAppDataStore
{
    private const string SettingsFile = "usage_config.json";
    private const string AppsFile = "tracked_apps.json";
    private const string SessionsFile = "activity_sessions.json";
    private const string AggregatesFile = "daily_aggregates.json";
    private const string CorrectionsFile = "manual_corrections.json";

    private readonly IAppDataPathProvider _pathProvider;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonAppDataStore(IAppDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public async Task<AppDataState> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_pathProvider.RootDirectory);
        return new AppDataState
        {
            Settings = await ReadOrDefaultAsync(
                SettingsFile,
                () => new AppSettings(),
                cancellationToken),
            Apps = await ReadOrDefaultAsync(
                AppsFile,
                () => new List<TrackedApp>(),
                cancellationToken),
            Sessions = await ReadOrDefaultAsync(
                SessionsFile,
                () => new List<ActivitySession>(),
                cancellationToken),
            Aggregates = await ReadOrDefaultAsync(
                AggregatesFile,
                () => new List<DailyAggregate>(),
                cancellationToken),
            Corrections = await ReadOrDefaultAsync(
                CorrectionsFile,
                () => new List<ManualCorrection>(),
                cancellationToken),
        };
    }

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(SettingsFile, settings, cancellationToken);

    public Task SaveAppsAsync(
        IReadOnlyCollection<TrackedApp> apps,
        CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(AppsFile, apps, cancellationToken);

    public async Task SaveSessionsAsync(
        IReadOnlyCollection<ActivitySession> sessions,
        CancellationToken cancellationToken = default)
    {
        foreach (var session in sessions)
        {
            if (session.EndedAtUtc < session.StartedAtUtc || session.DurationSeconds < 0)
            {
                throw new InvalidDataException($"会话 {session.Id} 的时间范围无效。");
            }
        }

        await WriteAtomicAsync(SessionsFile, sessions, cancellationToken);
    }

    public Task SaveAggregatesAsync(
        IReadOnlyCollection<DailyAggregate> aggregates,
        CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(AggregatesFile, aggregates, cancellationToken);

    public Task SaveCorrectionsAsync(
        IReadOnlyCollection<ManualCorrection> corrections,
        CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(CorrectionsFile, corrections, cancellationToken);

    public async Task<string> CreateBackupAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        Directory.CreateDirectory(_pathProvider.RootDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = GetUniqueBackupPath(destinationDirectory, timestamp);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(
                () => ZipFile.CreateFromDirectory(
                    _pathProvider.RootDirectory,
                    target,
                    CompressionLevel.Optimal,
                    false),
                cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        return target;
    }

    public async Task ImportBackupAsync(
        string backupFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFile))
        {
            throw new FileNotFoundException("备份文件不存在。", backupFile);
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var importDirectory = Path.Combine(
                Path.GetTempPath(),
                $"AppUsageTracker-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(importDirectory);
            try
            {
                ZipFile.ExtractToDirectory(backupFile, importDirectory);
                foreach (var file in Directory.EnumerateFiles(importDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = Path.Combine(_pathProvider.RootDirectory, Path.GetFileName(file));
                    Directory.CreateDirectory(_pathProvider.RootDirectory);
                    File.Copy(file, destination, true);
                }
            }
            finally
            {
                Directory.Delete(importDirectory, true);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        await ValidateFileAsync<AppSettings>(SettingsFile, errors, cancellationToken);
        await ValidateFileAsync<List<TrackedApp>>(AppsFile, errors, cancellationToken);
        await ValidateFileAsync<List<ActivitySession>>(SessionsFile, errors, cancellationToken);
        await ValidateFileAsync<List<DailyAggregate>>(AggregatesFile, errors, cancellationToken);
        await ValidateFileAsync<List<ManualCorrection>>(CorrectionsFile, errors, cancellationToken);
        return errors;
    }

    private async Task<T> ReadOrDefaultAsync<T>(
        string fileName,
        Func<T> defaultFactory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_pathProvider.RootDirectory, fileName);
        var value = await TryReadAsync<T>(path, cancellationToken);
        if (value is not null)
        {
            return value;
        }

        var backup = path + ".bak";
        value = await TryReadAsync<T>(backup, cancellationToken);
        return value ?? defaultFactory();
    }

    private async Task<T?> TryReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(
                stream,
                _jsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (IOException)
        {
            return default;
        }
    }

    private async Task WriteAtomicAsync<T>(
        string fileName,
        T value,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_pathProvider.RootDirectory);
            var path = Path.Combine(_pathProvider.RootDirectory, fileName);
            var temporary = path + ".tmp";
            var backup = path + ".bak";

            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    _jsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(path))
            {
                File.Copy(path, backup, true);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ValidateFileAsync<T>(
        string fileName,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_pathProvider.RootDirectory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            _ = await JsonSerializer.DeserializeAsync<T>(
                stream,
                _jsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            errors.Add($"{fileName}: {exception.Message}");
        }
    }

    private static string GetUniqueBackupPath(string destinationDirectory, string timestamp)
    {
        for (var suffix = 0; ; suffix++)
        {
            var name = suffix == 0
                ? $"AppUsageTracker-{timestamp}.zip"
                : $"AppUsageTracker-{timestamp}-{suffix}.zip";
            var path = Path.Combine(destinationDirectory, name);
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }
}
