using System.IO;

namespace AppUsageTracker.Services;

public sealed class AppDataPathProvider : IAppDataPathProvider
{
    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppUsageTracker");
}
