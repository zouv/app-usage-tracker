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
        AnchorDate = DateTime.Today;
        Refresh();
    }

    public ObservableCollection<AppUsageRow> Ranking { get; } = [];

    [ObservableProperty]
    private ChartData _dailyChart = ChartData.Empty;

    /// <summary>柱状图标题，随统计周期切换（每日 / 每周 / 各软件）。</summary>
    [ObservableProperty]
    private string _chartTitle = "每日使用时长";

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
    private string _selectedAppDetail = "点击排行中的软件查看详情";

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
    private void SelectPeriod(string period)
    {
        if (Enum.TryParse<StatisticsPeriod>(period, out var parsed))
        {
            SelectedPeriod = parsed;
        }
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
            $"{row.Name}：近7天 {DurationFormatter.Format(sevenDays)}，" +
            $"近30天 {DurationFormatter.Format(thirtyDays)}，" +
            $"累计 {DurationFormatter.Format(total)}";
    }

    public async void Refresh()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        StatisticsSnapshot snapshot;
        try
        {
            snapshot = await _runtime.Statistics.BuildAsync(
                SelectedPeriod,
                AnchorDate,
                _runtime.Sessions.ToList(),
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
        TopAppName = snapshot.TopApp?.Name ?? "暂无";
        LongestDuration = DurationFormatter.Format(snapshot.LongestSessionSeconds);
        ChangeText = $"{snapshot.PreviousPeriodChangePercentage:+0.0;-0.0;0}%";
        ChartTitle = SelectedPeriod switch
        {
            StatisticsPeriod.Day => "当日各软件使用时长",
            StatisticsPeriod.Year => "每周使用时长",
            StatisticsPeriod.All => "各软件累计使用时长",
            _ => "每日使用时长",
        };

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
            StatisticsPeriod.Day => start.ToString("yyyy年M月d日"),
            StatisticsPeriod.Month => start.ToString("yyyy年M月"),
            StatisticsPeriod.Year => start.ToString("yyyy年"),
            StatisticsPeriod.All => "全部时间",
            _ => $"{start:M月d日} - {end.AddDays(-1):M月d日}",
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
}
