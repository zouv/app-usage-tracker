using System.Diagnostics;
using System.IO;
using AppUsageTracker.Models;
using Drawing = System.Drawing;

namespace AppUsageTracker.Services;

public sealed class ProcessScanner : IProcessScanner
{
    public IReadOnlyList<RunningProcessInfo> EnumerateVisibleProcesses()
    {
        var result = new Dictionary<string, RunningProcessInfo>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return [];
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle == nint.Zero)
                    {
                        continue;
                    }

                    var path = TryGetPath(process);
                    var key = string.IsNullOrWhiteSpace(path)
                        ? process.ProcessName
                        : path;
                    result.TryAdd(
                        key,
                        new RunningProcessInfo(
                            process.Id,
                            EnsureExe(process.ProcessName),
                            path,
                            process.MainWindowTitle ?? string.Empty,
                            ReadIcon(path)));
                }
                catch
                {
                    // 进程可能已退出或权限不足。
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return result.Values
            .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string TryGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string EnsureExe(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

    private static string ReadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return string.Empty;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }
}
