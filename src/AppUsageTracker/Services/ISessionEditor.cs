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

    Task MergeAsync(
        Guid firstSessionId,
        Guid secondSessionId,
        CancellationToken cancellationToken = default);
}
