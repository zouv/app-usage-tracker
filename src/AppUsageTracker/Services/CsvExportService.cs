using System.Globalization;
using System.IO;
using System.Text;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class CsvExportService
{
    public async Task ExportSessionsAsync(
        string filePath,
        IReadOnlyCollection<ActivitySession> sessions,
        IReadOnlyCollection<TrackedApp> apps,
        CancellationToken cancellationToken = default)
    {
        var appMap = apps.ToDictionary(app => app.Id);
        var builder = new StringBuilder();
        builder.AppendLine("SessionId,Application,StartedAt,EndedAt,DurationSeconds,State,EndReason,IsManual");
        foreach (var session in sessions.OrderBy(item => item.StartedAtUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = session.ApplicationId is { } appId && appMap.TryGetValue(appId, out var app)
                ? app.Name
                : string.Empty;
            builder.AppendLine(string.Join(
                ",",
                Escape(session.Id.ToString()),
                Escape(name),
                Escape(session.StartedAtUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture)),
                Escape(session.EndedAtUtc?.ToLocalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
                session.DurationSeconds.ToString(CultureInfo.InvariantCulture),
                session.State,
                session.EndReason,
                session.IsManual));
        }

        await File.WriteAllTextAsync(
            filePath,
            builder.ToString(),
            new UTF8Encoding(true),
            cancellationToken);
    }

    private static string Escape(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
