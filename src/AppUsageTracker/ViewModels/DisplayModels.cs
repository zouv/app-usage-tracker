using System.Windows.Media;
using AppUsageTracker.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppUsageTracker.ViewModels;

public static class DurationFormatter
{
    public static string Format(long totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        var span = TimeSpan.FromSeconds(totalSeconds);
        var hours = Services.LocalizationService.T("Loc.Unit.Hours");
        var minutes = Services.LocalizationService.T("Loc.Unit.Minutes");
        var seconds = Services.LocalizationService.T("Loc.Unit.Seconds");
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}{hours}{span.Minutes:D2}{minutes}";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}{minutes}{span.Seconds:D2}{seconds}";
        }

        return $"{span.Seconds}{seconds}";
    }

    public static string FormatClock(long totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
    }
}

/// <summary>下拉选项：稳定值用于绑定与过滤，标签随界面语言本地化；Tooltip 可选，用于悬停说明。</summary>
public sealed record OptionItem(object Value, string Label, string? Tooltip = null);

public sealed partial class AppUsageRow : ObservableObject
{
    public Guid ApplicationId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    public string ColorHex { get; init; } = "#2F6BDE";

    public Brush ColorBrush => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(
            Services.AppColorPalette.Resolve(ColorHex)));

    /// <summary>软件真实图标；取不到时为 <c>null</c>，界面回退到首字母色块。</summary>
    public ImageSource? Icon { get; init; }

    /// <summary>没有图标时才显示首字母色块。</summary>
    public bool HasIcon => Icon is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration))]
    private long _seconds;

    [ObservableProperty]
    private double _percentage;

    public string Duration => DurationFormatter.Format(Seconds);
}

/// <summary>排行榜集合的增量同步：软件与排序不变时只更新时长，避免滚动位置被重置。</summary>
public static class AppUsageRowSync
{
    public static void Apply(
        System.Collections.ObjectModel.ObservableCollection<AppUsageRow> target,
        IReadOnlyList<AppUsageSummary> ranking,
        IReadOnlyDictionary<Guid, TrackedApp> appMap)
    {
        var fresh = ranking
            .Select(item => new AppUsageRow
            {
                ApplicationId = item.ApplicationId,
                Name = item.Name,
                Initials = OverviewViewModel.GetInitials(item.Name),
                ColorHex = item.ColorHex,
                Icon = Services.AppIconProvider.Resolve(
                    appMap.GetValueOrDefault(item.ApplicationId)),
                Seconds = item.Seconds,
                Percentage = item.Percentage,
            })
            .ToList();

        if (fresh.Count == target.Count &&
            fresh.Zip(target).All(pair =>
                pair.First.ApplicationId == pair.Second.ApplicationId &&
                pair.First.Name == pair.Second.Name &&
                pair.First.ColorHex == pair.Second.ColorHex &&
                pair.First.Icon == pair.Second.Icon))
        {
            for (var index = 0; index < fresh.Count; index++)
            {
                target[index].Seconds = fresh[index].Seconds;
                target[index].Percentage = fresh[index].Percentage;
            }

            return;
        }

        target.Clear();
        foreach (var row in fresh)
        {
            target.Add(row);
        }
    }
}

public sealed class SessionRow
{
    public required ActivitySession Session { get; init; }

    public string ApplicationName { get; init; } = string.Empty;

    public string Date => Session.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd");

    public string TimeRange =>
        $"{Session.StartedAtUtc.ToLocalTime():HH:mm:ss} - {Session.EndedAtUtc?.ToLocalTime():HH:mm:ss}";

    public string Duration => DurationFormatter.Format(Session.DurationSeconds);

    public bool IsActive => Session.EndedAtUtc is null;

    public string Source => IsActive
        ? Services.LocalizationService.T("Loc.Timeline.Ongoing")
        : Session.IsManual
            ? Services.LocalizationService.T("Loc.Timeline.Manual")
            : Services.LocalizationService.EndReasonLabel(Session.EndReason);
}

/// <summary>概览页「当前活跃软件」横向卡片条里的一张卡片。</summary>
public sealed partial class ActiveAppCard : ObservableObject
{
    public Guid ApplicationId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    public string ColorHex { get; init; } = "#2F6BDE";

    public Brush ColorBrush => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(
            Services.AppColorPalette.Resolve(ColorHex)));

    /// <summary>软件真实图标；取不到时为 <c>null</c>，界面回退到首字母色块。</summary>
    public ImageSource? Icon { get; init; }

    public bool HasIcon => Icon is not null;

    /// <summary>按软件统计模式显示的来源状态：有效 / 前台 / 运行。</summary>
    public string StateText { get; init; } = string.Empty;

    /// <summary>本次连续累计时长（时钟格式），每秒原地更新，避免重建整个卡片集合。</summary>
    [ObservableProperty]
    private string _continuousDuration = string.Empty;

    /// <summary>该软件今日累计时长，每秒原地更新。</summary>
    [ObservableProperty]
    private string _todayDuration = string.Empty;
}
