using System.IO;

namespace AppUsageTracker.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppUsageTracker",
        "logs");
    private static readonly string LogFile = Path.Combine(LogDirectory, "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(
                    LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不得影响主流程。
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogFile) || new FileInfo(LogFile).Length < 1024 * 1024)
        {
            return;
        }

        var archive = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.Move(LogFile, archive, true);
    }
}
