using Timer = System.Timers.Timer;

namespace AppUsageTracker.Services;

public sealed class UsageNotificationService : IDisposable
{
    private readonly AppRuntime _runtime;
    private readonly TrayIconService _tray;
    private readonly Timer _timer = new(TimeSpan.FromMinutes(1));
    private readonly Dictionary<Guid, int> _lastNotifiedHourBySession = [];
    private DateOnly? _lastSummaryDate;
    private bool _disposed;

    public UsageNotificationService(AppRuntime runtime, TrayIconService tray)
    {
        _runtime = runtime;
        _tray = tray;
        _timer.AutoReset = true;
        _timer.Elapsed += (_, _) => CheckSafely();
        _timer.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }

    private void CheckSafely()
    {
        try
        {
            Check();
        }
        catch (Exception exception)
        {
            // 通知失败不能影响监听与持久化。
            AppLogger.Error("使用提醒检查失败。", exception);
        }
    }

    private void Check()
    {
        if (_disposed || !_runtime.Settings.NotificationsEnabled)
        {
            return;
        }

        var snapshot = _runtime.Snapshot;
        var activeSessionIds = new HashSet<Guid>();
        // 每个活跃软件各自按整小时提醒一次连续使用；以会话 Id 为键，软件重新开始使用后重新计时。
        foreach (var info in snapshot.ActiveApps)
        {
            activeSessionIds.Add(info.Session.Id);
            var seconds = info.EstimateDurationSeconds(_runtime.TimeProvider.UtcNow);
            var hour = (int)(seconds / 3600);
            var lastHour = _lastNotifiedHourBySession.GetValueOrDefault(info.Session.Id);
            if (hour > 0 && hour > lastHour)
            {
                _lastNotifiedHourBySession[info.Session.Id] = hour;
                _tray.ShowNotification(
                    LocalizationService.T("Loc.Notify.ReminderTitle"),
                    LocalizationService.T(
                        "Loc.Notify.ReminderMessage",
                        info.App.Name,
                        hour));
            }
        }

        foreach (var sessionId in _lastNotifiedHourBySession.Keys
                     .Where(id => !activeSessionIds.Contains(id))
                     .ToList())
        {
            _lastNotifiedHourBySession.Remove(sessionId);
        }

        var localNow = DateTime.Now;
        var today = DateOnly.FromDateTime(localNow);
        if (ShouldFireDailySummary(
            localNow,
            _lastSummaryDate,
            _runtime.Settings.DailySummaryHour,
            _runtime.Settings.DailySummaryMinute))
        {
            // 与概览/统计使用同一份实时会话快照，避免漏掉当前仍在进行的会话。
            var summary = _runtime.Statistics.Build(
                Models.StatisticsPeriod.Day,
                localNow,
                _runtime.GetLiveSessionsSnapshot(),
                _runtime.AppsSnapshot);
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
