using System.Diagnostics;
using System.IO;
using AppUsageTracker.Models;
using Drawing = System.Drawing;

namespace AppUsageTracker.Services;

public sealed class ProcessScanner : IProcessScanner
{
    public IReadOnlyList<RunningProcessInfo> EnumerateRunningProcesses(
        IReadOnlyCollection<string>? processNames = null) =>
        EnumerateProcesses(visibleOnly: false, withIcon: false, processNames);

    public IReadOnlyList<RunningProcessInfo> EnumerateVisibleProcesses() =>
        EnumerateProcesses(visibleOnly: true, withIcon: true, processNames: null);

    private static IReadOnlyList<RunningProcessInfo> EnumerateProcesses(
        bool visibleOnly,
        bool withIcon,
        IReadOnlyCollection<string>? processNames)
    {
        var nameFilter = processNames is null
            ? null
            : new HashSet<string>(
                processNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(NormalizeProcessName),
                StringComparer.OrdinalIgnoreCase);
        if (nameFilter is { Count: 0 })
        {
            return [];
        }

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
                    if (nameFilter is not null &&
                        !nameFilter.Contains(NormalizeProcessName(process.ProcessName)))
                    {
                        continue;
                    }

                    if (visibleOnly && process.MainWindowHandle == nint.Zero)
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
                            // 运行模式匹配不读取窗口标题，避免每次扫描为所有进程访问主窗口。
                            visibleOnly ? process.MainWindowTitle ?? string.Empty : string.Empty,
                            withIcon ? ReadIcon(path) : string.Empty));
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

    /// <summary>
    /// 取进程可执行文件路径：MainModule 对受保护或 64 位进程会拒绝访问，
    /// 此时回退到 QueryFullProcessImageName（与前台监听同一套原生封装）。
    /// </summary>
    private static string TryGetPath(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }
        catch
        {
            // 进程可能已退出、权限不足或位数不匹配，走原生回退。
        }

        return QueryPath(process.Id);
    }

    private static string QueryPath(int processId)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            false,
            (uint)processId);
        if (handle == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            var size = 32768u;
            var buffer = new System.Text.StringBuilder((int)size);
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string EnsureExe(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

    private static string NormalizeProcessName(string name) =>
        Path.GetFileNameWithoutExtension(name.Trim());

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
