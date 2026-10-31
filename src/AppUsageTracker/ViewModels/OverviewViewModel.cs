using System.Collections.ObjectModel;
using System.Windows;
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
    private int _refreshPending;
    private StatisticsSnapshot? _statisticsCache;
    private Dictionary<Guid, long> _rankingCache = [];
    private DateTime _statisticsCachedAtUtc;

    public OverviewViewModel(AppRuntime runtime)
    {
        _runtime = runtime;
        _runtime.DataChanged += OnDataChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>页面是否处于当前可视内容中；离开概览页后暂停每秒刷新。</summary>
    public bool IsActive { get; set; }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs) => RequestRefresh();

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

    private void OnDataChanged(object? sender, EventArgs eventArgs) => RequestRefresh();

    private void Tick()
    {
        // 窗口隐藏、切换页面或没有活跃软件时不需要每秒重算完整统计。
        if (!IsActive ||
            !IsWindowVisible() ||
            _runtime.Snapshot.ActiveApps.Count == 0 ||
            Volatile.Read(ref _refreshPending) == 1)
        {
            return;
        }

        UpdateLiveDurations();
    }

    /// <summary>
    /// 每秒只更新活跃卡片的连续/今日时长和今日总计，完整统计仍由数据事件驱动。
    /// 这样避免每秒克隆全部历史会话并重算排行榜与时间线。
    /// </summary>
    private void UpdateLiveDurations()
    {
        var snapshot = _runtime.Snapshot;
        var statistics = _statisticsCache;
        if (statistics is null || ActiveApps.Count != snapshot.ActiveApps.Count)
        {
            Refresh();
            return;
        }

        var now = _runtime.TimeProvider.UtcNow;
        var liveTotal = statistics.TotalSeconds;
        foreach (var info in snapshot.ActiveApps)
        {
            var card = ActiveApps.FirstOrDefault(item => item.ApplicationId == info.App.Id);
            if (card is null)
            {
                Refresh();
                return;
            }

            card.ContinuousDuration = DurationFormatter.FormatClock(
                info.EstimateDurationSeconds(now));
            var from = info.Session.StartedAtUtc > _statisticsCachedAtUtc
                ? info.Session.StartedAtUtc
                : _statisticsCachedAtUtc;
            var extra = now > from ? (long)(now - from).TotalSeconds : 0;
            card.TodayDuration = DurationFormatter.Format(
                _rankingCache.GetValueOrDefault(info.App.Id) + extra);
            liveTotal += extra;
        }

        TodayTotalDuration = DurationFormatter.Format(liveTotal);
    }

    private static bool IsWindowVisible() =>
        Application.Current?.MainWindow is
            { IsVisible: true, WindowState: not WindowState.Minimized };

    /// <summary>合并后台数据事件与语言切换，只排队一次 UI 刷新，避免阻塞监听线程。</summary>
    private void RequestRefresh()
    {
        if (!IsActive || !IsWindowVisible())
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
        {
            return;
        }

        dispatcher.BeginInvoke(
            () =>
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                if (!IsActive || !IsWindowVisible())
                {
                    return;
                }

                Refresh();
            });
    }

    /// <summary>页面进入可视树时立即补一次数据。</summary>
    public void Activate()
    {
        IsActive = true;
        if (IsWindowVisible())
        {
            Refresh();
        }
    }

    public void Deactivate() => IsActive = false;

    private void Refresh()
    {
        var snapshot = _runtime.Snapshot;
        TodayLabel = LocalizationService.FullDate(DateTime.Today);
        CurrentStateText = StateText(snapshot.State);
        PauseButtonText = LocalizationService.T(
            snapshot.IsPaused ? "Loc.Overview.Resume" : "Loc.Overview.Pause");

        var sessions = _runtime.GetLiveSessionsSnapshot();
        var appMap = _runtime.Apps.ToDictionary(app => app.Id);
        var statistics = _runtime.Statistics.Build(
            StatisticsPeriod.Day,
            DateTime.Now,
            sessions,
            _runtime.Apps);
        _statisticsCache = statistics;
        _statisticsCachedAtUtc = _runtime.TimeProvider.UtcNow;
        _rankingCache = statistics.Ranking.ToDictionary(
            item => item.ApplicationId,
            item => item.Seconds);
        TodayTotalDuration = DurationFormatter.Format(statistics.TotalSeconds);
        TopAppName = statistics.TopApp?.Name ?? LocalizationService.T("Loc.Overview.None");
        LongestSession = DurationFormatter.Format(statistics.LongestSessionSeconds);
        var today = DateTime.Today;
        SwitchCount = $"{sessions.Count(item =>
                          item.StartedAtUtc.ToLocalTime().Date == today &&
                          item.EndReason == SessionEndReason.WindowChanged)}" +
                      LocalizationService.T("Loc.Unit.Times");

        var freshCards = snapshot.ActiveApps
            .Select(info => BuildCard(info, statistics))
            .ToList();
        if (freshCards.Count == ActiveApps.Count &&
            freshCards.Select(card => card.ApplicationId)
                .SequenceEqual(ActiveApps.Select(card => card.ApplicationId)) &&
            freshCards.Zip(ActiveApps).All(pair =>
                pair.First.Name == pair.Second.Name &&
                pair.First.ColorHex == pair.Second.ColorHex &&
                pair.First.StateText == pair.Second.StateText &&
                pair.First.Icon == pair.Second.Icon))
        {
            // 软件集合与静态信息没变时只刷新时长，保留横向滚动位置和卡片容器。
            for (var index = 0; index < freshCards.Count; index++)
            {
                ActiveApps[index].ContinuousDuration = freshCards[index].ContinuousDuration;
                ActiveApps[index].TodayDuration = freshCards[index].TodayDuration;
            }
        }
        else
        {
            Replace(ActiveApps, freshCards);
        }

        HasActiveApps = ActiveApps.Count > 0;

        AppUsageRowSync.Apply(Ranking, statistics.Ranking, appMap);
        TimelineChart = ChartBuilder.BuildTimeline(sessions, _runtime.Apps, DateTime.Today);
    }

    /// <summary>组装一张活跃卡片：来源状态按软件统计模式，连续时长取实时单调时钟，今日时长来自当日统计。</summary>
    private ActiveAppCard BuildCard(ActiveAppInfo info, StatisticsSnapshot statistics)
    {
        var now = _runtime.TimeProvider.UtcNow;
        var continuousSeconds = info.EstimateDurationSeconds(now);
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
    }
}
