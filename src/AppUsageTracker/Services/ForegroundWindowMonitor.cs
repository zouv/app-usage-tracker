using System.Diagnostics;
using System.IO;
using System.Text;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class ForegroundWindowMonitor : IForegroundWindowMonitor
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly System.Timers.Timer _verificationTimer = new(2000) { AutoReset = true };
    private nint _hook;
    private bool _disposed;

    public ForegroundWindowMonitor()
    {
        _callback = OnWinEvent;
        _verificationTimer.Elapsed += (_, _) =>
            ThreadPool.QueueUserWorkItem(_ => Publish(ReadWindow(NativeMethods.GetForegroundWindow())));
    }

    public event EventHandler<ForegroundWindowInfo?>? ForegroundWindowChanged;

    public ForegroundWindowInfo? Current { get; private set; }

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            NativeMethods.WineventOutofcontext);
        Publish(ReadWindow(NativeMethods.GetForegroundWindow()));
        _verificationTimer.Start();
    }

    public void Stop()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWinEvent(_hook);
        _hook = nint.Zero;
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
        uint eventTime)
    {
        ThreadPool.QueueUserWorkItem(_ => Publish(ReadWindow(window)));
    }

    private void Publish(ForegroundWindowInfo? window)
    {
        if (Current == window)
        {
            return;
        }

        Current = window;
        ForegroundWindowChanged?.Invoke(this, window);
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

        var titleBuffer = new StringBuilder(1024);
        _ = NativeMethods.GetWindowText(window, titleBuffer, titleBuffer.Capacity);
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
            titleBuffer.ToString());
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
