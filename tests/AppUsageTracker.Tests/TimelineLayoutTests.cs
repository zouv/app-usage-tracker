using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Tests;

public class TimelineLayoutTests
{
    private static readonly DateTime Day = new(2026, 8, 13, 0, 0, 0, DateTimeKind.Local);

    [Fact]
    public void ComputeWindow_AlignsToWholeHoursCoveringAllActivity()
    {
        var (start, end) = TimelineLayout.ComputeWindow(
            Day.AddHours(9).AddMinutes(37),
            Day.AddHours(18).AddMinutes(12),
            Day);

        Assert.Equal(Day.AddHours(9), start);
        Assert.Equal(Day.AddHours(19), end);
    }

    [Fact]
    public void ComputeWindow_ExpandsShortSpanToMinimumHours()
    {
        var (start, end) = TimelineLayout.ComputeWindow(
            Day.AddHours(10).AddMinutes(5),
            Day.AddHours(10).AddMinutes(20),
            Day);

        Assert.Equal(TimelineLayout.MinimumAxisHours, (end - start).TotalHours);
        Assert.Equal(Day.AddHours(10), start);
    }

    [Fact]
    public void ComputeWindow_ExpandsBackwardWhenActivityIsNearMidnight()
    {
        var (start, end) = TimelineLayout.ComputeWindow(
            Day.AddHours(23).AddMinutes(10),
            Day.AddHours(23).AddMinutes(50),
            Day);

        // 不得越过当天末尾，改为向前扩展。
        Assert.Equal(Day.AddDays(1), end);
        Assert.Equal(TimelineLayout.MinimumAxisHours, (end - start).TotalHours);
    }
}
