using AppUsageTracker.Models;
using AppUsageTracker.Services;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Tests;

public sealed class ViewModelInteractionTests
{
    [Fact]
    public void SelectionCommandsAreDisabledWithoutSelection()
    {
        var apps = new AppsViewModel(CreateRuntime(out _));

        Assert.False(apps.EditSelectedCommand.CanExecute(null));
        Assert.False(apps.DeleteSelectedCommand.CanExecute(null));
        Assert.False(apps.ToggleSelectedCommand.CanExecute(null));
        Assert.False(apps.UseSelectedProcessCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunningModeDoesNotSaveWindowTitleRule()
    {
        var apps = new AppsViewModel(CreateRuntime(out _));
        apps.AddNewCommand.Execute(null);
        apps.EditName = "Background worker";
        apps.EditProcessName = "worker.exe";
        apps.EditWindowTitle = "should not be used";
        apps.EditTrackingMode = TrackingMode.Running;

        Assert.False(apps.IsWindowTitleRuleAvailable);
        await apps.SaveEditorCommand.ExecuteAsync(null);

        var app = Assert.Single(apps.Apps);
        Assert.DoesNotContain(app.MatchRules, rule => rule.Type == MatchRuleType.WindowTitleContains);
    }

    [Fact]
    public void InvalidNumericSettingIsNotApplied()
    {
        var runtime = CreateRuntime(out var store);
        var settings = new SettingsViewModel(runtime, new StartupService(), store);

        settings.IdleThresholdMinutes = 0;

        Assert.Equal(5, runtime.Settings.IdleThresholdMinutes);
        Assert.NotEmpty(settings.ValidationMessage);
    }

    [Fact]
    public void ActiveSessionCommandsAreDisabled()
    {
        var runtime = CreateRuntime(out _);
        var timeline = new TimelineViewModel(
            runtime,
            new SessionEditor(runtime, runtime.TimeProvider),
            new CsvExportService());
        timeline.SelectedSession = new SessionRow
        {
            Session = new ActivitySession
            {
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                LastHeartbeatAtUtc = DateTime.UtcNow,
            },
        };

        Assert.False(timeline.EditSessionCommand.CanExecute(null));
        Assert.False(timeline.DeleteSessionCommand.CanExecute(null));
        Assert.False(timeline.MergeWithPreviousCommand.CanExecute(null));
        Assert.False(timeline.MarkAsIdleCommand.CanExecute(null));
        Assert.False(timeline.NextDayCommand.CanExecute(null));
    }

    [Fact]
    public void TimelineCommandsValidateAppsAndEditorTime()
    {
        var editor = new RecordingSessionEditor();
        var timeline = new TimelineViewModel(
            CreateRuntime(out _),
            editor,
            new CsvExportService());

        Assert.False(timeline.AddSessionCommand.CanExecute(null));

        timeline.Apps = [new TrackedApp { Name = "Editor", ProcessName = "editor.exe" }];
        timeline.EditApp = timeline.Apps[0];
        timeline.EditStart = new DateTime(2026, 10, 31, 9, 0, 0);
        timeline.EditEnd = timeline.EditStart;

        Assert.True(timeline.AddSessionCommand.CanExecute(null));
        Assert.False(timeline.SaveEditorCommand.CanExecute(null));
        Assert.NotEmpty(timeline.EditorError);

        timeline.EditEnd = timeline.EditStart.AddMinutes(30);

        Assert.True(timeline.SaveEditorCommand.CanExecute(null));
        Assert.Empty(timeline.EditorError);
        Assert.Empty(editor.AddedSessions);
    }

    [Fact]
    public void MergeRequiresSameApplication()
    {
        var timeline = new TimelineViewModel(
            CreateRuntime(out _),
            new RecordingSessionEditor(),
            new CsvExportService());
        var previous = CompletedRow(Guid.NewGuid());
        var selected = CompletedRow(Guid.NewGuid());
        timeline.Sessions.Add(previous);
        timeline.Sessions.Add(selected);
        timeline.SelectedSession = selected;

        Assert.False(timeline.MergeWithPreviousCommand.CanExecute(null));

        previous.Session.ApplicationId = selected.Session.ApplicationId;
        timeline.MergeWithPreviousCommand.NotifyCanExecuteChanged();

        Assert.True(timeline.MergeWithPreviousCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task DeleteSessionHonorsConfirmation(bool confirmed, int deleteCount)
    {
        var editor = new RecordingSessionEditor();
        var confirmation = new FakeConfirmationService(confirmed);
        var timeline = new TimelineViewModel(
            CreateRuntime(out _),
            editor,
            new CsvExportService(),
            confirmation);
        timeline.SelectedSession = CompletedRow(Guid.NewGuid());

        await timeline.DeleteSessionCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmation.CallCount);
        Assert.Equal(deleteCount, editor.DeletedIds.Count);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task MergeSessionHonorsConfirmation(bool confirmed, int mergeCount)
    {
        var appId = Guid.NewGuid();
        var editor = new RecordingSessionEditor();
        var confirmation = new FakeConfirmationService(confirmed);
        var timeline = new TimelineViewModel(
            CreateRuntime(out _),
            editor,
            new CsvExportService(),
            confirmation);
        var previous = CompletedRow(appId);
        var selected = CompletedRow(appId);
        timeline.Sessions.Add(previous);
        timeline.Sessions.Add(selected);
        timeline.SelectedSession = selected;

        await timeline.MergeWithPreviousCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmation.CallCount);
        Assert.Equal(mergeCount, editor.Merges.Count);
    }

    [Fact]
    public void StatisticsSelectionResetsWhenPeriodChanges()
    {
        var statistics = new StatisticsViewModel(CreateRuntime(out _));
        statistics.SelectedAppRow = new AppUsageRow { Name = "Editor" };

        Assert.Contains("Editor", statistics.SelectedAppDetail);

        statistics.SelectedPeriod = StatisticsPeriod.Day;

        Assert.Null(statistics.SelectedAppRow);
        Assert.Equal(LocalizationService.T("Loc.Statistics.DetailHint"), statistics.SelectedAppDetail);
    }

    [Fact]
    public void StatisticsRefreshReconcilesSelectionByApplicationId()
    {
        var statistics = new StatisticsViewModel(CreateRuntime(out _));
        var appId = Guid.NewGuid();
        statistics.Ranking.Add(new AppUsageRow { ApplicationId = appId, Name = "Before" });
        statistics.SelectedAppRow = statistics.Ranking[0];

        statistics.Ranking.Clear();
        statistics.Ranking.Add(new AppUsageRow { ApplicationId = appId, Name = "After" });
        statistics.ReconcileSelection(appId);

        Assert.Same(statistics.Ranking[0], statistics.SelectedAppRow);
        Assert.Contains("After", statistics.SelectedAppDetail);

        statistics.Ranking.Clear();
        statistics.ReconcileSelection(appId);

        Assert.Null(statistics.SelectedAppRow);
        Assert.Equal(LocalizationService.T("Loc.Statistics.DetailHint"), statistics.SelectedAppDetail);
    }

    [Theory]
    [InlineData(StatisticsPeriod.Day)]
    [InlineData(StatisticsPeriod.Week)]
    [InlineData(StatisticsPeriod.Month)]
    [InlineData(StatisticsPeriod.Year)]
    public void NextPeriodStopsAtCurrentPeriod(StatisticsPeriod period)
    {
        var today = new DateTime(2026, 10, 31);

        Assert.False(StatisticsViewModel.CanNavigateNext(today, period, today));
        Assert.True(StatisticsViewModel.CanNavigateNext(today.AddYears(-1), period, today));
        Assert.False(StatisticsViewModel.CanNavigateNext(today, StatisticsPeriod.All, today));
    }

    private static SessionRow CompletedRow(Guid applicationId)
    {
        var start = new DateTime(2026, 10, 31, 8, 0, 0, DateTimeKind.Utc);
        return new SessionRow
        {
            ApplicationName = "Editor",
            Session = new ActivitySession
            {
                ApplicationId = applicationId,
                StartedAtUtc = start,
                EndedAtUtc = start.AddMinutes(30),
                LastHeartbeatAtUtc = start.AddMinutes(30),
            },
        };
    }

    private static AppRuntime CreateRuntime(out IAppDataStore store)
    {
        var memoryStore = new MemoryAppDataStore();
        store = memoryStore;
        return new AppRuntime(
            memoryStore,
            new FakeTimeProvider(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc)),
            new ApplicationMatcher(),
            new FakeProcessScanner());
    }
}
