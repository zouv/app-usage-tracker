namespace AppUsageTracker.Models;

public sealed class AppSettings
{
    public bool AutoStart { get; set; } = true;

    public bool StartMinimized { get; set; }

    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>呼出/隐藏主窗口的全局快捷键，形如 Ctrl+Alt+T。</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+T";

    public bool ExcludeIdleTime { get; set; } = true;

    public int IdleThresholdMinutes { get; set; } = 5;

    public int ShortSwitchSeconds { get; set; } = 5;

    public int HeartbeatSeconds { get; set; } = 15;

    public int AutoSaveSeconds { get; set; } = 60;

    public int MaxHistoryDays { get; set; } = 365;

    public string Theme { get; set; } = "System";

    public bool NotificationsEnabled { get; set; } = true;

    public bool SaveWindowTitles { get; set; }
}
