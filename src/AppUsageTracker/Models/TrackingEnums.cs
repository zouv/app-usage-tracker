namespace AppUsageTracker.Models;

public enum TrackingMode
{
    Effective,
    Foreground,
    Running,
}

public enum ActivityState
{
    Stopped,
    Untracked,
    Active,
    Idle,
    Locked,
    Suspended,
    Paused,
    Private,
}

public enum SessionEndReason
{
    None,
    WindowChanged,
    Idle,
    Locked,
    Suspended,
    Paused,
    PrivateMode,
    Disabled,
    Midnight,
    ApplicationExit,
    Recovered,
    Manual,
}

public enum MatchRuleType
{
    ExecutablePath,
    ProcessName,
    WindowTitleContains,
    WindowTitleRegex,
    ExcludeWindowTitleContains,
}
