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
}
