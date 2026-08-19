namespace AppUsageTracker.Services;

public interface IAppDataPathProvider
{
    string RootDirectory { get; }
}
