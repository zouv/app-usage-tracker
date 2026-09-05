namespace AppUsageTracker.Models;

public sealed class TrackedApp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string Category { get; set; } = "未分类";

    public string ColorHex { get; set; } = "#2F6BDE";

    public string IconBase64 { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public TrackingMode TrackingMode { get; set; } = TrackingMode.Effective;

    public bool IgnoreIdle { get; set; }

    public int? IdleThresholdMinutes { get; set; }

    public List<string> RelatedProcessNames { get; set; } = [];

    public List<MatchRule> MatchRules { get; set; } = [];

    public string Notes { get; set; } = string.Empty;

    /// <summary>悬停提示用的配置摘要，随界面语言本地化；仅用于显示，不参与持久化。</summary>
    public string ConfigSummary
    {
        get
        {
            var modeLabel = Services.LocalizationService.TrackingModeLabel(TrackingMode);
            var enabledLabel = Services.LocalizationService.T(Enabled
                ? "Loc.Apps.Status.Enabled"
                : "Loc.Apps.Status.Disabled");
            var lines = new List<string>
            {
                $"{Services.LocalizationService.T("Loc.Apps.Field.Name")}: {Name}",
                $"{Services.LocalizationService.T("Loc.Apps.Field.Process")}: {(string.IsNullOrWhiteSpace(ProcessName) ? "—" : ProcessName)}",
                $"{Services.LocalizationService.T("Loc.Apps.Field.Path")}: {(string.IsNullOrWhiteSpace(ExecutablePath) ? "—" : ExecutablePath)}",
                $"{Services.LocalizationService.T("Loc.Apps.Field.Category")}: {Services.LocalizationService.CategoryLabel(Category)}",
                $"{Services.LocalizationService.T("Loc.Apps.Field.Mode")}: {modeLabel}",
                $"{Services.LocalizationService.T("Loc.Apps.Field.EnableListen")}: {enabledLabel}",
                $"{Services.LocalizationService.T("Loc.Apps.Field.IgnoreIdle")}: {(IgnoreIdle ? Services.LocalizationService.T("Loc.Common.Yes") : Services.LocalizationService.T("Loc.Common.No"))}",
            };
            if (!string.IsNullOrWhiteSpace(Notes))
            {
                lines.Add($"{Services.LocalizationService.T("Loc.Apps.Field.Notes")}: {Notes}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
