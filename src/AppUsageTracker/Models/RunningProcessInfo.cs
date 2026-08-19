namespace AppUsageTracker.Models;

public sealed record RunningProcessInfo(
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    string WindowTitle,
    string IconBase64 = "");
