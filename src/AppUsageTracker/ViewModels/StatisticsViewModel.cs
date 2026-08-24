using System.Collections.ObjectModel;
using AppUsageTracker.Models;
using AppUsageTracker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppUsageTracker.ViewModels;

public partial class StatisticsViewModel : ObservableObject
{
    private readonly AppRuntime _runtime;
    private CancellationTokenSource? _refreshCancellation;

    public StatisticsViewModel(AppRuntime runtime)
    {
        _runtime = runtime;
        _runtime.DataChanged += (_, _) => App.Current.Dispatcher.Invoke(Refresh);
        LocalizationService.LanguageChanged += (_, _) => Refresh();
        AnchorDate = DateTime.Today;
        Refresh();
    }

    public ObservableCollection<AppUsageRow> Ranking { get; } = [];

    [ObservableProperty]
    private ChartData _dailyChart = ChartData.Empty;

    /// <summary>柱状图标题，随统计周期切换（每日 / 每周 / 各软件）。</summary>
    [ObservableProperty]
    private string _chartTitle = LocalizationService.T("Loc.Statistics.ChartDaily");

    [ObservableProperty]
    private string? _highlightedSeriesKey;

    [ObservableProperty]
    private StatisticsPeriod _selectedPeriod = StatisticsPeriod.Week;

    partial void OnSelectedPeriodChanged(StatisticsPeriod value) => Refresh();

    [ObservableProperty]
    private DateTime _anchorDate;

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
    private string _selectedAppDetail = LocalizationService.T("Loc.Statistics.DetailHint");

    [RelayCommand]
    private void PreviousPeriod()
    {
        AnchorDate = Shift(AnchorDate, SelectedPeriod, -1);
        Refresh();
    }

    [RelayCommand]
    private void NextPeriod()
    {
        AnchorDate = Shift(AnchorDate, SelectedPeriod, 1);
        Refresh();
    }

    [RelayCommand]
    private void SelectApp(AppUsageRow? row)
    {
        if (row is null)
        {
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
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        StatisticsSnapshot snapshot;
        try
        {
            var sessions = BuildLiveSessions();
            snapshot = await _runtime.Statistics.BuildAsync(
                SelectedPeriod,
                AnchorDate,
                sessions,
                _runtime.Apps.ToList(),
                _refreshCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        RangeLabel = FormatRange(snapshot.RangeStartLocal, snapshot.RangeEndLocal, SelectedPeriod);
        TotalDuration = DurationFormatter.Format(snapshot.TotalSeconds);
        AverageDuration = DurationFormatter.Format(snapshot.AverageDailySeconds);
        TopAppName = snapshot.TopApp?.Name ?? LocalizationService.T("Loc.Overview.None");
        LongestDuration = DurationFormatter.Format(snapshot.LongestSessionSeconds);
        ChangeText = $"{snapshot.PreviousPeriodChangePercentage:+0.0;-0.0;0}%";
        ChartTitle = LocalizationService.T(SelectedPeriod switch
        {
            StatisticsPeriod.Day => "Loc.Statistics.ChartDayByApp",
            StatisticsPeriod.Year => "Loc.Statistics.ChartWeekly",
            StatisticsPeriod.All => "Loc.Statistics.ChartAllByApp",
            _ => "Loc.Statistics.ChartDaily",
        });

        var appMap = _runtime.Apps.ToDictionary(app => app.Id);
        Replace(
            Ranking,
            snapshot.Ranking.Select(item => new AppUsageRow
            {
                ApplicationId = item.ApplicationId,
                Name = item.Name,
                Initials = OverviewViewModel.GetInitials(item.Name),
                ColorHex = item.ColorHex,
                Icon = AppIconProvider.Resolve(appMap.GetValueOrDefault(item.ApplicationId)),
                Seconds = item.Seconds,
                Percentage = item.Percentage,
            }));

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
}
