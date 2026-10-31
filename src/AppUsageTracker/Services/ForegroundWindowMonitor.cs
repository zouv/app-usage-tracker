using System.Diagnostics;
using System.IO;
using System.Text;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class ForegroundWindowMonitor : IForegroundWindowMonitor
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly System.Timers.Timer _verificationTimer = new(2000) { AutoReset = true };
    private readonly object _publishLock = new();
    private long _requestVersion;
    private long _publishedVersion;
    private nint _hook;
    private bool _active;
    private bool _disposed;

    public ForegroundWindowMonitor()
    {
        _callback = OnWinEvent;
        _verificationTimer.Elapsed += (_, _) => VerifyForeground();
    }

    public event EventHandler<ForegroundWindowInfo?>? ForegroundWindowChanged;

    public ForegroundWindowInfo? Current { get; private set; }

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        lock (_publishLock)
        {
            _active = true;
        }

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            NativeMethods.WineventOutofcontext);
        QueueRead(NativeMethods.GetForegroundWindow());
        _verificationTimer.Start();
    }

    public void Stop()
    {
        lock (_publishLock)
        {
            _active = false;
        }

        if (_hook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = nint.Zero;
        }

        _verificationTimer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _verificationTimer.Dispose();
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime) =>
        QueueRead(window);

    /// <summary>兜底校验：窗口没变时只重读标题，避免每 2 秒重复打开进程、查询完整路径。</summary>
    private void VerifyForeground()
    {
        // 版本号在读取前分配：之后发生的真实窗口事件会拿到更大的版本并覆盖这次兜底结果。
        var version = NextVersion();
        var handle = NativeMethods.GetForegroundWindow();
        var current = Current;
        if (current is not null && current.WindowHandle == handle)
        {
            var title = ReadTitle(handle);
            if (!string.Equals(title, current.WindowTitle, StringComparison.Ordinal))
            {
                QueuePublish(current with { WindowTitle = title }, version);
            }

            return;
        }

        QueueRead(handle, version);
    }

    private long NextVersion() => Interlocked.Increment(ref _requestVersion);

    private void QueueRead(nint window) => QueueRead(window, NextVersion());

    private void QueueRead(nint window, long version) =>
        ThreadPool.QueueUserWorkItem(_ => Publish(ReadWindow(window), version));

    private void QueuePublish(ForegroundWindowInfo? window, long version) =>
        ThreadPool.QueueUserWorkItem(_ => Publish(window, version));

    /// <summary>
    /// 带版本号发布，丢弃比已发布结果更旧的异步读取，避免快速切换窗口时旧窗口覆盖新窗口。
    /// 事件在锁内触发以保持与版本号一致的顺序；处理方只做入队与状态机调用，不会回调本监视器。
    /// </summary>
    private void Publish(ForegroundWindowInfo? window, long version)
    {
        lock (_publishLock)
        {
            if (!_active || version <= _publishedVersion)
            {
                return;
            }

            _publishedVersion = version;
            if (Equals(Current, window))
            {
                return;
            }

            Current = window;
            ForegroundWindowChanged?.Invoke(this, window);
        }
    }

    private static string ReadTitle(nint window)
    {
        if (window == nint.Zero)
        {
            return string.Empty;
        }

        var titleBuffer = new StringBuilder(1024);
        _ = NativeMethods.GetWindowText(window, titleBuffer, titleBuffer.Capacity);
        return titleBuffer.ToString();
    }

    private static ForegroundWindowInfo? ReadWindow(nint window)
    {
        if (window == nint.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var title = ReadTitle(window);
        var executablePath = QueryPath(processId);
        var processName = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName + ".exe";
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                processName = Path.GetFileName(executablePath);
            }
        }

        return new ForegroundWindowInfo(
            window,
            (int)processId,
            processName,
            executablePath,
            title);
    }

    private static string QueryPath(uint processId)
    {
        var process = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            false,
            processId);
        if (process == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            var size = 32768u;
            var buffer = new StringBuilder((int)size);
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size)
                ? buffer.ToString()
                : string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
