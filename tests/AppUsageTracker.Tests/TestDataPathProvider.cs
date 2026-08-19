using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

internal sealed class TestDataPathProvider : IAppDataPathProvider, IDisposable
{
    public TestDataPathProvider()
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(),
            "AppUsageTracker.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, true);
        }
    }
}
