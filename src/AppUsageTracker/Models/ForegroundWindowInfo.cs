namespace AppUsageTracker.Models;

public sealed record ForegroundWindowInfo(
    nint WindowHandle,
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    string WindowTitle);
