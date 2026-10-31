using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public interface ISessionEditor
{
    Task<ActivitySession> AddAsync(
        ActivitySession session,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ActivitySession session,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>批量删除已结束会话（跳过进行中的会话），返回实际删除数量。</summary>
    Task<int> DeleteRangeAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);

    Task MergeAsync(
        Guid firstSessionId,
        Guid secondSessionId,
        CancellationToken cancellationToken = default);
}
