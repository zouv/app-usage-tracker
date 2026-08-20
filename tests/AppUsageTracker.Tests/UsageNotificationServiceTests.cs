using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class UsageNotificationServiceTests
{
    [Fact]
    public void DailySummaryDoesNotFireBeforeConfiguredTime()
    {
        var now = new DateTime(2026, 8, 20, 17, 59, 0);

        Assert.False(UsageNotificationService.ShouldFireDailySummary(now, null, 18, 0));
    }

    [Fact]
    public void DailySummaryFiresAtConfiguredTime()
    {
        var now = new DateTime(2026, 8, 20, 18, 0, 0);

        Assert.True(UsageNotificationService.ShouldFireDailySummary(now, null, 18, 0));
    }

    [Fact]
    public void DailySummaryFiresAfterConfiguredTimeWhenNotYetSent()
    {
        var now = new DateTime(2026, 8, 20, 23, 30, 0);

        Assert.True(UsageNotificationService.ShouldFireDailySummary(now, null, 18, 0));
    }

    [Fact]
    public void DailySummaryDoesNotFireTwiceOnSameDay()
    {
        var now = new DateTime(2026, 8, 20, 20, 0, 0);
        var lastSent = new DateOnly(2026, 8, 20);

        Assert.False(UsageNotificationService.ShouldFireDailySummary(now, lastSent, 18, 0));
    }

    [Fact]
    public void DailySummaryFiresAgainOnNextDay()
    {
        var now = new DateTime(2026, 8, 21, 18, 30, 0);
        var lastSent = new DateOnly(2026, 8, 20);

        Assert.True(UsageNotificationService.ShouldFireDailySummary(now, lastSent, 18, 0));
    }

    [Fact]
    public void DailySummaryHonorsCustomMinute()
    {
        var now = new DateTime(2026, 8, 20, 9, 30, 0);

        Assert.True(UsageNotificationService.ShouldFireDailySummary(now, null, 9, 30));
        Assert.False(UsageNotificationService.ShouldFireDailySummary(now, null, 9, 31));
    }

    [Fact]
    public void DailySummaryClampsOutOfRangeTime()
    {
        // 小时与分钟越界时按 23:59 处理，仍应在当日末尾触发。
        var now = new DateTime(2026, 8, 20, 23, 59, 0);

        Assert.True(UsageNotificationService.ShouldFireDailySummary(now, null, 25, 99));
    }
}
