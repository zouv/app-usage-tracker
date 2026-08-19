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
        Refresh();
    }

    public ObservableCollection<AppUsageRow> Ranking { get; } = [];

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
        CurrentAppName = snapshot.CurrentApp?.Name ??
                         (snapshot.State == ActivityState.Untracked ? "未配置软件" : "暂无活动");
        CurrentAppInitials = GetInitials(CurrentAppName);
        CurrentStateText = StateText(snapshot.State);
        PauseButtonText = snapshot.IsPaused ? "继续监听" : "暂停监听";

        var sessions = BuildLiveSessions();
        var appMap = _runtime.Apps.ToDictionary(app => app.Id);
        var statistics = _runtime.Statistics.Build(
            StatisticsPeriod.Day,
            DateTime.Now,
            sessions,
            _runtime.Apps);
        TodayTotalDuration = DurationFormatter.Format(statistics.TotalSeconds);
        TopAppName = statistics.TopApp?.Name ?? "暂无";
        LongestSession = DurationFormatter.Format(statistics.LongestSessionSeconds);
        SwitchCount = $"{sessions.Count(item => item.EndReason == SessionEndReason.WindowChanged)}次";
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
        state switch
        {
            ActivityState.Active => "前台活跃",
            ActivityState.Idle => "用户空闲",
            ActivityState.Locked => "系统锁屏",
            ActivityState.Suspended => "系统休眠",
            ActivityState.Paused => "监听已暂停",
            ActivityState.Private => "隐私模式",
            ActivityState.Untracked => "当前软件未配置",
            ActivityState.Stopped => "监听未启动",
            _ => state.ToString(),
        };

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
        _runtime.DataChanged -= OnDataChanged;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
    }
}
