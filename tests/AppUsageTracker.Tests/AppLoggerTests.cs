using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class AppLoggerTests
{
    [Fact]
    public void PruneArchivesKeepsOnlyNewestFiles()
    {
        using var paths = new TestDataPathProvider();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 13; index++)
        {
            var path = Path.Combine(paths.RootDirectory, $"app-20260101-{index:D6}.log");
            File.WriteAllText(path, "log");
            File.SetLastWriteTimeUtc(path, start.AddMinutes(index));
        }

        AppLogger.PruneArchives(paths.RootDirectory, keep: 10);

        var remaining = Directory.GetFiles(paths.RootDirectory, "app-*.log")
            .Select(Path.GetFileName)
            .Order()
            .ToArray();
        Assert.Equal(10, remaining.Length);
        Assert.DoesNotContain("app-20260101-000000.log", remaining);
        Assert.DoesNotContain("app-20260101-000001.log", remaining);
        Assert.DoesNotContain("app-20260101-000002.log", remaining);
        Assert.Contains("app-20260101-000012.log", remaining);
    }
}
