namespace AppUsageTracker.Models;

public sealed class ActivitySession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ApplicationId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public long DurationSeconds { get; set; }

    public ActivityState State { get; set; } = ActivityState.Active;

    public SessionEndReason EndReason { get; set; }

    public DateTime LastHeartbeatAtUtc { get; set; }

    public bool IsManual { get; set; }

    public string Notes { get; set; } = string.Empty;

    public ActivitySession Clone() => (ActivitySession)MemberwiseClone();
}
