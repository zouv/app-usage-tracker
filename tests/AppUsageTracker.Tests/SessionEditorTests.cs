using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class SessionEditorTests
{
    [Fact]
    public async Task AddUpdateDeleteCreatesCorrectionsAndPersists()
    {
        var time = new FakeTimeProvider(
            new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc));
        var store = new MemoryAppDataStore();
        await using var runtime = new AppRuntime(
            store,
            time,
            new ApplicationMatcher(),
            new FakeProcessScanner());
        var app = new TrackedApp { Name = "Editor" };
        runtime.Apps.Add(app);
        var editor = new SessionEditor(runtime, time);
        var session = new ActivitySession
        {
            ApplicationId = app.Id,
            StartedAtUtc = time.UtcNow,
            EndedAtUtc = time.UtcNow.AddMinutes(30),
        };

        await editor.AddAsync(session);
        Assert.Equal(1800, runtime.Sessions.Single().DurationSeconds);
        Assert.Equal("Add", runtime.Corrections.Single().Operation);

        session.EndedAtUtc = time.UtcNow.AddMinutes(45);
        await editor.UpdateAsync(session);
        Assert.Equal(2700, runtime.Sessions.Single().DurationSeconds);
        Assert.Equal("Update", runtime.Corrections.Last().Operation);

        await editor.DeleteAsync(session.Id);
        Assert.Empty(runtime.Sessions);
        Assert.Equal("Delete", runtime.Corrections.Last().Operation);
    }

    [Fact]
    public async Task MergeRequiresSameApplication()
    {
        var time = new FakeTimeProvider(DateTime.UtcNow);
        await using var runtime = new AppRuntime(
            new MemoryAppDataStore(),
            time,
            new ApplicationMatcher(),
            new FakeProcessScanner());
        var editor = new SessionEditor(runtime, time);
        runtime.Sessions.AddRange(
        [
            new ActivitySession
            {
                ApplicationId = Guid.NewGuid(),
                StartedAtUtc = time.UtcNow,
                EndedAtUtc = time.UtcNow.AddMinutes(1),
            },
            new ActivitySession
            {
                ApplicationId = Guid.NewGuid(),
                StartedAtUtc = time.UtcNow.AddMinutes(2),
                EndedAtUtc = time.UtcNow.AddMinutes(3),
            },
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => editor.MergeAsync(runtime.Sessions[0].Id, runtime.Sessions[1].Id));
    }
}
