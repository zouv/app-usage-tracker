using System.Collections.ObjectModel;
using System.Windows;
using AppUsageTracker.Models;
using AppUsageTracker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppUsageTracker.ViewModels;

public partial class StatisticsViewModel : ObservableObject
{
    private readonly AppRuntime _runtime;
    private CancellationTokenSource? _refreshCancellation;
    private int _refreshPending;
    private long _refreshVersion;
    private bool _isActive;

    public StatisticsViewModel(AppRuntime runtime)
    {
        _runtime = runtime;
        _runtime.DataChanged += (_, _) => RequestRefresh();
        LocalizationService.LanguageChanged += (_, _) => RequestRefresh();
        AnchorDate = DateTime.Today;
    }

    /// <summary>页面是否处于当前可视内容中；离开本页后只标记待刷新，不再后台重算。</summary>
    public bool IsActive => _isActive;

    public void Activate()
    {
        _isActive = true;
        Refresh();
    }

    public void Deactivate() => _isActive = false;

    private void RequestRefresh()
    {
        if (!_isActive || !IsWindowVisible())
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
                if (_isActive && IsWindowVisible())
                {
                    Refresh();
                }
            });
    }

    private static bool IsWindowVisible() =>
        Application.Current?.MainWindow is
            { IsVisible: true, WindowState: not WindowState.Minimized };

    public ObservableCollection<AppUsageRow> Ranking { get; } = [];

    [ObservableProperty]
    private ChartData _dailyChart = ChartData.Empty;

    /// <summary>柱状图标题，随统计周期切换（每日 / 每周 / 各软件）。</summary>
    [ObservableProperty]
    private string _chartTitle = LocalizationService.T("Loc.Statistics.ChartDaily");

    [ObservableProperty]
    private string? _highlightedSeriesKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPeriodCommand))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextPeriod))]
    private StatisticsPeriod _selectedPeriod = StatisticsPeriod.Week;

    partial void OnSelectedPeriodChanged(StatisticsPeriod value)
    {
        SelectedAppRow = null;
        Refresh();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPeriodCommand))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextPeriod))]
    private DateTime _anchorDate;

    public bool CanGoToNextPeriod => CanNavigateNext(AnchorDate, SelectedPeriod, DateTime.Today);

    [ObservableProperty]
    private string _rangeLabel = string.Empty;

    [ObservableProperty]
    private string _totalDuration = "0秒";

    [ObservableProperty]
    private string _averageDuration = "0秒";

    [ObservableProperty]
    private string _topAppName = "暂无";

    [ObservableProperty]
    private string _longestDuration = "0秒";

    [ObservableProperty]
    private string _changeText = "0%";

    [ObservableProperty]
    private string _changeKind = "muted";

    [ObservableProperty]
    private AppUsageRow? _selectedAppRow;

    partial void OnSelectedAppRowChanged(AppUsageRow? value) => UpdateSelectedAppDetail(value);

    [ObservableProperty]
    private string _selectedAppDetail = LocalizationService.T("Loc.Statistics.DetailHint");

    [RelayCommand]
    private void PreviousPeriod()
    {
        SelectedAppRow = null;
        AnchorDate = Shift(AnchorDate, SelectedPeriod, -1);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPeriod))]
    private void NextPeriod()
    {
        if (!CanGoToNextPeriod)
        {
            return;
        }

        SelectedAppRow = null;
        AnchorDate = Shift(AnchorDate, SelectedPeriod, 1);
        Refresh();
    }

    private void UpdateSelectedAppDetail(AppUsageRow? row)
    {
        if (row is null)
        {
            SelectedAppDetail = LocalizationService.T("Loc.Statistics.DetailHint");
            return;
        }

        var appSessions = _runtime.Sessions
            .Where(item => item.ApplicationId == row.ApplicationId && item.EndedAtUtc is not null)
            .ToList();
        var sevenDays = appSessions
            .Where(item => item.StartedAtUtc.ToLocalTime() >= DateTime.Now.AddDays(-7))
            .Sum(item => item.DurationSeconds);
        var thirtyDays = appSessions
            .Where(item => item.StartedAtUtc.ToLocalTime() >= DateTime.Now.AddDays(-30))
            .Sum(item => item.DurationSeconds);
        var total = appSessions.Sum(item => item.DurationSeconds);
        SelectedAppDetail =
            $"{row.Name}：" +
            $"{LocalizationService.T("Loc.Statistics.Last7Days")} {DurationFormatter.Format(sevenDays)}，" +
            $"{LocalizationService.T("Loc.Statistics.Last30Days")} {DurationFormatter.Format(thirtyDays)}，" +
            $"{LocalizationService.T("Loc.Statistics.TotalAccumulated")} {DurationFormatter.Format(total)}";
    }

    public async void Refresh()
    {
        if (!_isActive || !IsWindowVisible())
        {
            return;
        }

        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var version = Interlocked.Increment(ref _refreshVersion);
        StatisticsSnapshot snapshot;
        try
        {
            snapshot = await _runtime.Statistics.BuildAsync(
                SelectedPeriod,
                AnchorDate,
                _runtime.GetLiveSessionsSnapshot(),
                _runtime.Apps.ToList(),
                _refreshCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            AppLogger.Error("统计刷新失败。", exception);
            return;
        }

        // 快速切换周期时，旧计算可能晚于新计算返回；只允许最新一次结果落到界面。
        if (version != Volatile.Read(ref _refreshVersion))
        {
            return;
        }

        RangeLabel = FormatRange(snapshot.RangeStartLocal, snapshot.RangeEndLocal, SelectedPeriod);
        TotalDuration = DurationFormatter.Format(snapshot.TotalSeconds);
        AverageDuration = DurationFormatter.Format(snapshot.AverageDailySeconds);
        TopAppName = snapshot.TopApp?.Name ?? LocalizationService.T("Loc.Overview.None");
        LongestDuration = DurationFormatter.Format(snapshot.LongestSessionSeconds);
        ChangeText = $"{snapshot.PreviousPeriodChangePercentage:+0.0;-0.0;0}%";
        ChangeKind = snapshot.PreviousPeriodChangePercentage switch
        {
            > 0 => "success",
            < 0 => "danger",
            _ => "muted",
        };
        ChartTitle = LocalizationService.T(SelectedPeriod switch
        {
            StatisticsPeriod.Day => "Loc.Statistics.ChartDayByApp",
            StatisticsPeriod.Year => "Loc.Statistics.ChartWeekly",
            StatisticsPeriod.All => "Loc.Statistics.ChartAllByApp",
            _ => "Loc.Statistics.ChartDaily",
        });

        var appMap = _runtime.Apps.ToDictionary(app => app.Id);
        var selectedAppId = SelectedAppRow?.ApplicationId;
        AppUsageRowSync.Apply(Ranking, snapshot.Ranking, appMap);
        ReconcileSelection(selectedAppId);

        DailyChart = SelectedPeriod switch
        {
            StatisticsPeriod.Day => ChartBuilder.BuildByApp(snapshot.DailyPoints),
            StatisticsPeriod.Year => ChartBuilder.BuildWeekly(
                snapshot.DailyPoints,
                snapshot.RangeStartLocal,
                snapshot.RangeEndLocal),
            StatisticsPeriod.All => ChartBuilder.BuildByApp(snapshot.DailyPoints),
            _ => ChartBuilder.BuildDaily(
                snapshot.DailyPoints,
                snapshot.RangeStartLocal,
                snapshot.RangeEndLocal),
        };
    }

    /// <summary>排行榜实例更新后按稳定软件 Id 恢复选择，并刷新详情文案。</summary>
    internal void ReconcileSelection(Guid? selectedAppId)
    {
        SelectedAppRow = selectedAppId is { } id
            ? Ranking.FirstOrDefault(row => row.ApplicationId == id)
            : null;
        UpdateSelectedAppDetail(SelectedAppRow);
    }

    internal static bool CanNavigateNext(
        DateTime anchor,
        StatisticsPeriod period,
        DateTime today)
    {
        if (period == StatisticsPeriod.All)
        {
            return false;
        }

        return Shift(PeriodStart(anchor, period), period, 1) <= PeriodStart(today, period);
    }

    private static DateTime PeriodStart(DateTime value, StatisticsPeriod period) => period switch
    {
        StatisticsPeriod.Day => value.Date,
        StatisticsPeriod.Week => value.Date.AddDays(-(((int)value.DayOfWeek + 6) % 7)),
        StatisticsPeriod.Month => new DateTime(value.Year, value.Month, 1),
        StatisticsPeriod.Year => new DateTime(value.Year, 1, 1),
        _ => value.Date,
    };

    private static DateTime Shift(DateTime value, StatisticsPeriod period, int direction) =>
        period switch
        {
            StatisticsPeriod.Day => value.AddDays(direction),
            StatisticsPeriod.Week => value.AddDays(7 * direction),
            StatisticsPeriod.Month => value.AddMonths(direction),
            StatisticsPeriod.Year => value.AddYears(direction),
            StatisticsPeriod.All => value,
            _ => value,
        };

    private static string FormatRange(
        DateTime start,
        DateTime end,
        StatisticsPeriod period) =>
        period switch
        {
            StatisticsPeriod.Day => LocalizationService.FullDate(start),
            StatisticsPeriod.Month => LocalizationService.MonthYear(start),
            StatisticsPeriod.Year => LocalizationService.Year(start),
            StatisticsPeriod.All => LocalizationService.T("Loc.Statistics.AllTime"),
            _ => LocalizationService.IsEnglish
                ? $"{start:MMM d} - {end.AddDays(-1):MMM d}"
                : $"{start:M月d日} - {end.AddDays(-1):M月d日}",
        };

}
