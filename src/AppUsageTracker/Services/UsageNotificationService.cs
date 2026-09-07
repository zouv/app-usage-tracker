using Timer = System.Timers.Timer;

namespace AppUsageTracker.Services;

public sealed class UsageNotificationService : IDisposable
{
    private readonly AppRuntime _runtime;
    private readonly TrayIconService _tray;
    private readonly Timer _timer = new(TimeSpan.FromMinutes(1));
    private readonly Dictionary<Guid, int> _lastNotifiedHourByApp = [];
    private DateOnly? _lastSummaryDate;

    public UsageNotificationService(AppRuntime runtime, TrayIconService tray)
    {
        _runtime = runtime;
        _tray = tray;
        _timer.AutoReset = true;
        _timer.Elapsed += (_, _) => Check();
        _timer.Start();
    }

    public void Dispose() => _timer.Dispose();

    private void Check()
    {
        if (!_runtime.Settings.NotificationsEnabled)
        {
            return;
        }

        var snapshot = _runtime.Snapshot;
        // 每个活跃软件各自按整小时提醒一次连续使用。
        foreach (var info in snapshot.ActiveApps)
        {
            var seconds = Math.Max(
                info.Session.DurationSeconds,
                (long)(_runtime.TimeProvider.UtcNow - info.Session.StartedAtUtc).TotalSeconds);
            var hour = (int)(seconds / 3600);
            var lastHour = _lastNotifiedHourByApp.GetValueOrDefault(info.App.Id);
            if (hour > 0 && hour > lastHour)
            {
                _lastNotifiedHourByApp[info.App.Id] = hour;
                _tray.ShowNotification(
                    LocalizationService.T("Loc.Notify.ReminderTitle"),
                    LocalizationService.T(
                        "Loc.Notify.ReminderMessage",
                        info.App.Name,
                        hour));
            }
        }

        var localNow = DateTime.Now;
        var today = DateOnly.FromDateTime(localNow);
        if (ShouldFireDailySummary(
            localNow,
            _lastSummaryDate,
            _runtime.Settings.DailySummaryHour,
            _runtime.Settings.DailySummaryMinute))
        {
            var summary = _runtime.Statistics.Build(
                Models.StatisticsPeriod.Day,
                localNow,
                _runtime.Sessions,
                _runtime.Apps);
            if (summary.TotalSeconds > 0)
            {
                _lastSummaryDate = today;
                _tray.ShowNotification(
                    LocalizationService.T("Loc.Notify.SummaryTitle"),
                    LocalizationService.T(
                        "Loc.Notify.SummaryMessage",
                        ViewModels.DurationFormatter.Format(summary.TotalSeconds),
                        summary.TopApp?.Name ?? LocalizationService.T("Loc.Overview.None")));
            }
        }
    }

    /// <summary>判断当前时刻是否应弹出今日摘要：到达配置时间、且当日尚未弹过。</summary>
    public static bool ShouldFireDailySummary(
        DateTime localNow,
        DateOnly? lastSummaryDate,
        int hour,
        int minute)
    {
        var today = DateOnly.FromDateTime(localNow);
        if (lastSummaryDate == today)
        {
            return false;
        }

        var fireTime = new TimeOnly(
            Math.Clamp(hour, 0, 23),
            Math.Clamp(minute, 0, 59));
        return TimeOnly.FromDateTime(localNow) >= fireTime;
    }
}
