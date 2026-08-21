using System.Windows.Media;
using AppUsageTracker.Models;

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

/// <summary>下拉选项：稳定值用于绑定与过滤，标签随界面语言本地化。</summary>
public sealed record OptionItem(object Value, string Label);

public sealed class AppUsageRow
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

    public long Seconds { get; init; }

    public string Duration => DurationFormatter.Format(Seconds);

    public double Percentage { get; init; }
}

public sealed class SessionRow
{
    public required ActivitySession Session { get; init; }

    public string ApplicationName { get; init; } = string.Empty;

    public string Date => Session.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd");

    public string TimeRange =>
        $"{Session.StartedAtUtc.ToLocalTime():HH:mm:ss} - {Session.EndedAtUtc?.ToLocalTime():HH:mm:ss}";

    public string Duration => DurationFormatter.Format(Session.DurationSeconds);

    public string Source => Session.IsManual
        ? Services.LocalizationService.T("Loc.Timeline.Manual")
        : Services.LocalizationService.EndReasonLabel(Session.EndReason);
}
