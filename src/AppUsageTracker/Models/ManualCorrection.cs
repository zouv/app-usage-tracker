namespace AppUsageTracker.Models;

public sealed class ManualCorrection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public string Operation { get; set; } = string.Empty;

    public ActivitySession? Before { get; set; }

    public ActivitySession? After { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
