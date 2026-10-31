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
        PrepareManualSession(session);
        await _runtime.ActivityService.EditSessionsAsync(
            sessions =>
            {
                sessions.Add(session);
                return session;
            },
            cancellationToken);
        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = session.Id,
            Operation = "Add",
            After = session.Clone(),
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await SaveAndNotifyAsync(cancellationToken);
        return session;
    }

    public async Task UpdateAsync(
        ActivitySession session,
        CancellationToken cancellationToken = default)
    {
        Validate(session);
        PrepareManualSession(session);
        var before = await _runtime.ActivityService.EditSessionsAsync(
            sessions =>
            {
                var index = sessions.FindIndex(item => item.Id == session.Id);
                if (index < 0)
                {
                    throw new KeyNotFoundException("会话不存在。");
                }

                var existing = sessions[index];
                EnsureCompleted(existing);
                var previous = existing.Clone();
                sessions[index] = session;
                return previous;
            },
            cancellationToken);
        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = session.Id,
            Operation = "Update",
            Before = before,
            After = session.Clone(),
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await SaveAndNotifyAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var deleted = await _runtime.ActivityService.EditSessionsAsync(
            sessions =>
            {
                var index = sessions.FindIndex(item => item.Id == sessionId);
                if (index < 0)
                {
                    return null;
                }

                var existing = sessions[index];
                EnsureCompleted(existing);
                sessions.RemoveAt(index);
                return existing.Clone();
            },
            cancellationToken);
        if (deleted is null)
        {
            return;
        }

        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = deleted.Id,
            Operation = "Delete",
            Before = deleted,
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await SaveAndNotifyAsync(cancellationToken);
    }

    public async Task<int> DeleteRangeAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _runtime.ActivityService.EditSessionsAsync(
            sessions =>
            {
                var removed = new List<ActivitySession>();
                foreach (var id in sessionIds.Distinct())
                {
                    var index = sessions.FindIndex(item => item.Id == id);
                    if (index < 0)
                    {
                        continue;
                    }

                    var existing = sessions[index];
                    if (existing.EndedAtUtc is null)
                    {
                        // 进行中的会话不在原始列表中直接删除，避免与状态机失去同步。
                        continue;
                    }

                    sessions.RemoveAt(index);
                    removed.Add(existing.Clone());
                }

                return removed;
            },
            cancellationToken);
        if (deleted.Count == 0)
        {
            return 0;
        }

        foreach (var session in deleted)
        {
            _runtime.Corrections.Add(new ManualCorrection
            {
                SessionId = session.Id,
                Operation = "Delete",
                Before = session,
                CreatedAtUtc = _timeProvider.UtcNow,
            });
        }

        await SaveAndNotifyAsync(cancellationToken);
        return deleted.Count;
    }

    public async Task MergeAsync(
        Guid firstSessionId,
        Guid secondSessionId,
        CancellationToken cancellationToken = default)
    {
        var result = await _runtime.ActivityService.EditSessionsAsync(
            sessions =>
            {
                var firstIndex = sessions.FindIndex(item => item.Id == firstSessionId);
                var secondIndex = sessions.FindIndex(item => item.Id == secondSessionId);
                if (firstIndex < 0 || secondIndex < 0)
                {
                    throw new KeyNotFoundException("会话不存在。");
                }

                var first = sessions[firstIndex];
                var second = sessions[secondIndex];
                if (first.ApplicationId != second.ApplicationId)
                {
                    throw new InvalidOperationException("只能合并同一软件的两个会话。");
                }

                EnsureCompleted(first);
                EnsureCompleted(second);

                var merged = first.Clone();
                merged.StartedAtUtc = first.StartedAtUtc < second.StartedAtUtc
                    ? first.StartedAtUtc
                    : second.StartedAtUtc;
                merged.EndedAtUtc = Later(first.EndedAtUtc, second.EndedAtUtc);
                merged.DurationSeconds = first.DurationSeconds + second.DurationSeconds;
                merged.IsManual = true;
                merged.LastHeartbeatAtUtc = merged.EndedAtUtc ?? merged.StartedAtUtc;
                sessions.RemoveAll(item => item.Id == first.Id || item.Id == second.Id);
                sessions.Add(merged);
                return (First: first.Clone(), Merged: merged.Clone());
            },
            cancellationToken);
        _runtime.Corrections.Add(new ManualCorrection
        {
            SessionId = result.Merged.Id,
            Operation = "Merge",
            Before = result.First,
            After = result.Merged,
            CreatedAtUtc = _timeProvider.UtcNow,
        });
        await SaveAndNotifyAsync(cancellationToken);
    }

    private static void PrepareManualSession(ActivitySession session)
    {
        session.IsManual = true;
        session.DurationSeconds = CalculateDuration(session);
        session.LastHeartbeatAtUtc = session.EndedAtUtc ?? session.StartedAtUtc;
    }

    private static void EnsureCompleted(ActivitySession session)
    {
        if (session.EndedAtUtc is null)
        {
            throw new InvalidOperationException("进行中的会话不能修改或删除。");
        }
    }

    private async Task SaveAndNotifyAsync(CancellationToken cancellationToken)
    {
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
        if (session.EndedAtUtc is null)
        {
            throw new InvalidDataException("手工会话必须有结束时间。");
        }

        if (session.EndedAtUtc < session.StartedAtUtc)
        {
            throw new InvalidDataException("结束时间不能早于开始时间。");
        }
    }
}
