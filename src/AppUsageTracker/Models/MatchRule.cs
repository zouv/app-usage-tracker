namespace AppUsageTracker.Models;

public sealed class MatchRule
{
    public MatchRuleType Type { get; set; }

    public string Pattern { get; set; } = string.Empty;

    public int Priority { get; set; }
}
