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

    /// <summary>界面语言，取值 zh-CN（中文）或 en-US（English），默认中文。</summary>
    public string Language { get; set; } = "zh-CN";

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>每日摘要通知的触发小时（0-23）。</summary>
    public int DailySummaryHour { get; set; } = 18;

    /// <summary>每日摘要通知的触发分钟（0-59）。</summary>
    public int DailySummaryMinute { get; set; }

    public bool SaveWindowTitles { get; set; }
}
