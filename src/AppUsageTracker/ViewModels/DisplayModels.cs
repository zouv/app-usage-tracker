using System.Windows.Media;
using AppUsageTracker.Models;

namespace AppUsageTracker.ViewModels;

public static class DurationFormatter
{
    public static string Format(long totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        var span = TimeSpan.FromSeconds(totalSeconds);
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}小时{span.Minutes:D2}分";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}分{span.Seconds:D2}秒";
        }

        return $"{span.Seconds}秒";
    }

    public static string FormatClock(long totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
    }
}

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

    public string Source => Session.IsManual ? "手动" : Session.EndReason.ToString();
}
