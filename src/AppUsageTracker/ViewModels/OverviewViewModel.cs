using System.Collections.ObjectModel;
using System.Windows.Threading;
using AppUsageTracker.Models;
using AppUsageTracker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppUsageTracker.ViewModels;

public partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly AppRuntime _runtime;
    private readonly DispatcherTimer _timer;

    public OverviewViewModel(AppRuntime runtime)
    {
        _runtime = runtime;
        _runtime.DataChanged += OnDataChanged;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs) => Refresh();

    public ObservableCollection<AppUsageRow> Ranking { get; } = [];

    [ObservableProperty]
    private string _todayLabel = string.Empty;

    [ObservableProperty]
    private ChartData _timelineChart = ChartData.Empty;

    [ObservableProperty]
    private string? _highlightedSeriesKey;

    [ObservableProperty]
    private string _currentAppName = "未配置软件";

    [ObservableProperty]
    private string _currentAppInitials = "--";

    [ObservableProperty]
    private string _currentStateText = "等待监听";

    [ObservableProperty]
    private string _currentSessionDuration = "00:00:00";

    [ObservableProperty]
    private string _currentAppTodayDuration = "0秒";

    [ObservableProperty]
    private string _todayTotalDuration = "0秒";

    [ObservableProperty]
    private string _topAppName = "暂无";

    [ObservableProperty]
    private string _switchCount = "0次";

    [ObservableProperty]
    private string _longestSession = "0秒";

    [ObservableProperty]
    private string _pauseButtonText = "暂停监听";

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        await _runtime.ActivityService.SetPausedAsync(!_runtime.Snapshot.IsPaused);
    }

    private void OnDataChanged(object? sender, EventArgs eventArgs) =>
        App.Current.Dispatcher.Invoke(Refresh);

    private void OnSnapshotChanged(object? sender, TrackingSnapshot snapshot) =>
        App.Current.Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        var snapshot = _runtime.Snapshot;
        TodayLabel = LocalizationService.FullDate(DateTime.Today);
        CurrentAppName = snapshot.CurrentApp?.Name ??
                         (snapshot.State == ActivityState.Untracked
                             ? LocalizationService.T("Loc.Overview.Untracked")
                             : LocalizationService.T("Loc.Overview.NoActivity"));
        CurrentAppInitials = GetInitials(CurrentAppName);
        CurrentStateText = StateText(snapshot.State);
        PauseButtonText = LocalizationService.T(
            snapshot.IsPaused ? "Loc.Overview.Resume" : "Loc.Overview.Pause");

        var sessions = BuildLiveSessions();
        var appMap = _runtime.Apps.ToDictionary(app => app.Id);
        var statistics = _runtime.Statistics.Build(
            StatisticsPeriod.Day,
            DateTime.Now,
            sessions,
            _runtime.Apps);
        TodayTotalDuration = DurationFormatter.Format(statistics.TotalSeconds);
        TopAppName = statistics.TopApp?.Name ?? LocalizationService.T("Loc.Overview.None");
        LongestSession = DurationFormatter.Format(statistics.LongestSessionSeconds);
        SwitchCount = $"{sessions.Count(item => item.EndReason == SessionEndReason.WindowChanged)}" +
                      LocalizationService.T("Loc.Unit.Times");
        var currentAppSeconds = snapshot.CurrentApp is null
            ? 0
            : statistics.Ranking
                .FirstOrDefault(item => item.ApplicationId == snapshot.CurrentApp.Id)?.Seconds ?? 0;
        CurrentAppTodayDuration = DurationFormatter.Format(currentAppSeconds);

        if (snapshot.CurrentSession is { } current)
        {
            CurrentSessionDuration = DurationFormatter.FormatClock(
                Math.Max(
                    current.DurationSeconds,
                    (long)(_runtime.TimeProvider.UtcNow - current.StartedAtUtc).TotalSeconds));
        }
        else
        {
            CurrentSessionDuration = "00:00:00";
        }

        Replace(
            Ranking,
            statistics.Ranking.Select(item => new AppUsageRow
            {
                ApplicationId = item.ApplicationId,
                Name = item.Name,
                Initials = GetInitials(item.Name),
                ColorHex = item.ColorHex,
                Icon = AppIconProvider.Resolve(appMap.GetValueOrDefault(item.ApplicationId)),
                Seconds = item.Seconds,
                Percentage = item.Percentage,
            }));
        TimelineChart = ChartBuilder.BuildTimeline(sessions, _runtime.Apps, DateTime.Today);
    }

    private List<ActivitySession> BuildLiveSessions()
    {
        var sessions = _runtime.Sessions.Select(item => item.Clone()).ToList();
        if (_runtime.Snapshot.CurrentSession is { } current)
        {
            var existing = sessions.FirstOrDefault(item => item.Id == current.Id);
            if (existing is not null)
            {
                existing.EndedAtUtc = _runtime.TimeProvider.UtcNow;
                existing.DurationSeconds = Math.Max(
                    current.DurationSeconds,
                    (long)(_runtime.TimeProvider.UtcNow - current.StartedAtUtc).TotalSeconds);
            }
        }

        return sessions;
    }

    internal static string GetInitials(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant()
            : value.Length >= 2 ? value[..2].ToUpperInvariant() : value.ToUpperInvariant();
    }

    private static string StateText(ActivityState state) =>
        LocalizationService.ActivityStateLabel(state);

    private static void Replace<T>(
        ObservableCollection<T> collection,
        IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _runtime.DataChanged -= OnDataChanged;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
    }
}
