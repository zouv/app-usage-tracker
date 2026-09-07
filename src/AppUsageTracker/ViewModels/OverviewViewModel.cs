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

    /// <summary>当前正在累计的活跃软件卡片，可能有多个（前台 + 后台运行模式）。</summary>
    public ObservableCollection<ActiveAppCard> ActiveApps { get; } = [];

    [ObservableProperty]
    private bool _hasActiveApps;

    [ObservableProperty]
    private string _todayLabel = string.Empty;

    [ObservableProperty]
    private ChartData _timelineChart = ChartData.Empty;

    [ObservableProperty]
    private string? _highlightedSeriesKey;

    [ObservableProperty]
    private string _currentStateText = "等待监听";

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

        Replace(
            ActiveApps,
            snapshot.ActiveApps.Select(info => BuildCard(info, statistics)));
        HasActiveApps = ActiveApps.Count > 0;

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

    /// <summary>组装一张活跃卡片：来源状态按软件统计模式，连续时长取实时单调时钟，今日时长来自当日统计。</summary>
    private ActiveAppCard BuildCard(ActiveAppInfo info, StatisticsSnapshot statistics)
    {
        var now = _runtime.TimeProvider.UtcNow;
        var continuousSeconds = Math.Max(
            info.Session.DurationSeconds,
            (long)(now - info.Session.StartedAtUtc).TotalSeconds);
        var todaySeconds = statistics.Ranking
            .FirstOrDefault(item => item.ApplicationId == info.App.Id)?.Seconds ?? 0;
        return new ActiveAppCard
        {
            ApplicationId = info.App.Id,
            Name = info.App.Name,
            Initials = GetInitials(info.App.Name),
            ColorHex = info.App.ColorHex,
            Icon = AppIconProvider.Resolve(info.App),
            StateText = LocalizationService.TrackingModeLabel(info.SourceMode),
            ContinuousDuration = DurationFormatter.FormatClock(continuousSeconds),
            TodayDuration = DurationFormatter.Format(todaySeconds),
        };
    }

    private List<ActivitySession> BuildLiveSessions()
    {
        var sessions = _runtime.Sessions.Select(item => item.Clone()).ToList();
        var now = _runtime.TimeProvider.UtcNow;
        foreach (var info in _runtime.Snapshot.ActiveApps)
        {
            var existing = sessions.FirstOrDefault(item => item.Id == info.Session.Id);
            if (existing is not null)
            {
                existing.EndedAtUtc = now;
                existing.DurationSeconds = Math.Max(
                    info.Session.DurationSeconds,
                    (long)(now - info.Session.StartedAtUtc).TotalSeconds);
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
