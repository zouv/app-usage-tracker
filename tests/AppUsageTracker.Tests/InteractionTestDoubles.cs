using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

internal sealed class FakeConfirmationService(bool result) : IConfirmationService
{
    public int CallCount { get; private set; }

    public string LastMessage { get; private set; } = string.Empty;

    public string LastTitle { get; private set; } = string.Empty;

    public bool Confirm(string message, string title)
    {
        CallCount++;
        LastMessage = message;
        LastTitle = title;
        return result;
    }
}

internal sealed class RecordingSessionEditor : ISessionEditor
{
    public List<ActivitySession> AddedSessions { get; } = [];

    public List<Guid> DeletedIds { get; } = [];

    public List<(Guid FirstId, Guid SecondId)> Merges { get; } = [];

    public Task<ActivitySession> AddAsync(
        ActivitySession session,
        CancellationToken cancellationToken = default)
    {
        AddedSessions.Add(session);
        return Task.FromResult(session);
    }

    public Task UpdateAsync(
        ActivitySession session,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        DeletedIds.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task<int> DeleteRangeAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        DeletedIds.AddRange(sessionIds);
        return Task.FromResult(sessionIds.Count);
    }

    public Task MergeAsync(
        Guid firstSessionId,
        Guid secondSessionId,
        CancellationToken cancellationToken = default)
    {
        Merges.Add((firstSessionId, secondSessionId));
        return Task.CompletedTask;
    }
}
