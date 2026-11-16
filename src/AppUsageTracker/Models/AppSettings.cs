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

    /// <summary>倒计时时长（秒），由时/分/秒输入合成，至少 1 秒。</summary>
    public int TimerDurationSeconds { get; set; } = 1500;

    /// <summary>旧版「总分钟」字段：仅用于旧配置一次性迁移到 <see cref="TimerDurationSeconds"/>，迁移后清零。</summary>
    public int TimerDurationMinutes { get; set; }

    /// <summary>倒计时运行期间是否在桌面置顶显示可拖拽面板。</summary>
    public bool TimerShowPanel { get; set; } = true;

    /// <summary>倒计时面板宽度（像素），60–480；字号随宽度自适应。</summary>
    public int TimerPanelWidth { get; set; } = 220;

    /// <summary>面板文字不透明度（百分比），20–100。</summary>
    public int TimerPanelTextOpacity { get; set; } = 100;

    /// <summary>面板底板不透明度（百分比），0–100；0 为只剩悬浮文字。</summary>
    public int TimerPanelBaseOpacity { get; set; } = 90;

    /// <summary>旧版整窗不透明度字段：仅用于一次性迁移到文字/底板不透明度，迁移后清零。</summary>
    public int TimerPanelOpacity { get; set; }

    /// <summary>全屏桌布底板不透明度（百分比），10–100；桌布文字保持不透明。</summary>
    public int TimerOverlayOpacity { get; set; } = 50;

    /// <summary>倒计时结束后自动执行的动作。</summary>
    public TimerEndAction TimerEndAction { get; set; } = TimerEndAction.LockScreen;

    /// <summary>面板拖拽后的位置（屏幕坐标）；null 表示使用默认位置。</summary>
    public double? TimerPanelLeft { get; set; }

    /// <summary>面板拖拽后的位置（屏幕坐标）；null 表示使用默认位置。</summary>
    public double? TimerPanelTop { get; set; }
}

/// <summary>倒计时结束动作：系统锁屏，或全屏半透明桌布（Esc 关闭）。</summary>
public enum TimerEndAction
{
    LockScreen,
    ScreenOverlay,
}
