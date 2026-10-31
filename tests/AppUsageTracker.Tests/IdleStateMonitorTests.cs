using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class IdleStateMonitorTests
{
    [Fact]
    public void ElapsedMillisecondsHandlesTickCountWrap()
    {
        // 系统运行超过两个 49.7 天周期后，GetTickCount64 已是 64 位大数，
        // 而 GetLastInputInfo 仍只保存低 32 位；直接相减会把回绕算成空闲数十天。
        const ulong cycle = 1UL << 32;
        const ulong now = 2 * cycle + 1000;
        const uint lastInput = unchecked((uint)(1000 - 4000));

        var elapsed = IdleStateMonitor.CalculateElapsedMilliseconds(now, lastInput);

        Assert.Equal(4000u, elapsed);
    }

    [Fact]
    public void ElapsedMillisecondsUsesCurrentTickWithinSameWrap()
    {
        var elapsed = IdleStateMonitor.CalculateElapsedMilliseconds(50_000, 20_000);

        Assert.Equal(30_000u, elapsed);
    }
}
