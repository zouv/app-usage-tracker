using System.IO;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class SessionEditor : ISessionEditor
{
    private readonly AppRuntime _runtime;
    private readonly ITimeProvider _timeProvider;

    public SessionEditor(AppRuntime runtime, ITimeProvider timeProvider)
    {
        _runtime = runtime;
        _timeProvider = timeProvider;
    }

    public async Task<ActivitySession> AddAsync(
        ActivitySession session,
        CancellationToken cancellationToken = default)
    {
        Validate(session);
        session.IsManual = true;
        session.DurationSeconds = CalculateDuration(session);
        session.LastHeartbeatAtUtc = session.EndedAtUtc ?? session.StartedAtUtc;
        _runtime.Sessions.Add(session);
        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = session.Id,
            Operation = "Add",
            After = session.Clone(),
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await _runtime.SaveSessionsAndCorrectionsAsync(cancellationToken);
        _runtime.NotifyDataChanged();
        return session;
    }

    public async Task UpdateAsync(
        ActivitySession session,
        CancellationToken cancellationToken = default)
    {
        Validate(session);
        var index = _runtime.Sessions.FindIndex(item => item.Id == session.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException("会话不存在。");
        }

        var before = _runtime.Sessions[index].Clone();
        session.IsManual = true;
        session.DurationSeconds = CalculateDuration(session);
        _runtime.Sessions[index] = session;
        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = session.Id,
            Operation = "Update",
            Before = before,
            After = session.Clone(),
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await _runtime.SaveSessionsAndCorrectionsAsync(cancellationToken);
        _runtime.NotifyDataChanged();
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = _runtime.Sessions.FirstOrDefault(item => item.Id == sessionId);
        if (session is null)
        {
            return;
        }

        _runtime.Sessions.Remove(session);
        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = session.Id,
            Operation = "Delete",
            Before = session.Clone(),
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await _runtime.SaveSessionsAndCorrectionsAsync(cancellationToken);
        _runtime.NotifyDataChanged();
    }

    public async Task MergeAsync(
        Guid firstSessionId,
        Guid secondSessionId,
        CancellationToken cancellationToken = default)
    {
        var first = _runtime.Sessions.FirstOrDefault(item => item.Id == firstSessionId);
        var second = _runtime.Sessions.FirstOrDefault(item => item.Id == secondSessionId);
        if (first is null || second is null || first.ApplicationId != second.ApplicationId)
        {
            throw new InvalidOperationException("只能合并同一软件的两个会话。");
        }

        var merged = first.Clone();
        merged.StartedAtUtc = first.StartedAtUtc < second.StartedAtUtc
            ? first.StartedAtUtc
            : second.StartedAtUtc;
        merged.EndedAtUtc = Later(first.EndedAtUtc, second.EndedAtUtc);
        merged.DurationSeconds = first.DurationSeconds + second.DurationSeconds;
        merged.IsManual = true;
        _runtime.Sessions.Remove(first);
        _runtime.Sessions.Remove(second);
        _runtime.Sessions.Add(merged);
        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = merged.Id,
            Operation = "Merge",
            Before = first.Clone(),
            After = merged.Clone(),
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await _runtime.SaveSessionsAndCorrectionsAsync(cancellationToken);
        _runtime.NotifyDataChanged();
    }

    private static DateTime? Later(DateTime? left, DateTime? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left > right ? left : right;
    }

    private static long CalculateDuration(ActivitySession session) =>
        session.EndedAtUtc is null
            ? session.DurationSeconds
            : Math.Max(0, (long)(session.EndedAtUtc.Value - session.StartedAtUtc).TotalSeconds);

    private static void Validate(ActivitySession session)
    {
        if (session.EndedAtUtc < session.StartedAtUtc)
        {
            throw new InvalidDataException("结束时间不能早于开始时间。");
        }
    }
}
