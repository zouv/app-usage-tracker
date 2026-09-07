using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Tests;

public sealed class ApplicationMatcherTests
{
    private readonly ApplicationMatcher _matcher = new();

    [Fact]
    public void ExactPathWinsOverProcessName()
    {
        var pathApp = new TrackedApp
        {
            Name = "Path App",
            ExecutablePath = @"C:\Apps\tool.exe",
        };
        var processApp = new TrackedApp
        {
            Name = "Process App",
            ProcessName = "tool.exe",
        };
        var window = new ForegroundWindowInfo(
            1,
            1,
            "tool.exe",
            @"C:\Apps\tool.exe",
            "Tool");

        var result = _matcher.Match(window, [processApp, pathApp]);

        Assert.Equal(pathApp.Id, result?.Id);
    }

    [Fact]
    public void ExcludedTitlePreventsMatch()
    {
        var app = new TrackedApp
        {
            Name = "Browser",
            ProcessName = "chrome.exe",
            MatchRules =
            [
                new MatchRule
                {
                    Type = MatchRuleType.ExcludeWindowTitleContains,
                    Pattern = "Private",
                },
            ],
        };
        var window = new ForegroundWindowInfo(1, 1, "chrome.exe", string.Empty, "Private tab");

        Assert.Null(_matcher.Match(window, [app]));
    }

    [Fact]
    public void RelatedProcessNameMatches()
    {
        var app = new TrackedApp
        {
            Name = "Game",
            ProcessName = "launcher.exe",
            RelatedProcessNames = ["game-win64.exe"],
        };
        var window = new ForegroundWindowInfo(1, 1, "game-win64.exe", string.Empty, "Game");

        Assert.Equal(app.Id, _matcher.Match(window, [app])?.Id);
    }

    [Fact]
    public void MatchRunningProcessReturnsAllRunningModeApps()
    {
        var first = new TrackedApp
        {
            Name = "DaemonA",
            ProcessName = "daemon-a.exe",
            TrackingMode = TrackingMode.Running,
        };
        var second = new TrackedApp
        {
            Name = "DaemonB",
            ProcessName = "daemon-b.exe",
            TrackingMode = TrackingMode.Running,
        };
        // 非运行模式即使进程在跑也不应被返回。
        var foregroundOnly = new TrackedApp
        {
            Name = "Editor",
            ProcessName = "daemon-a.exe",
            TrackingMode = TrackingMode.Foreground,
        };
        RunningProcessInfo[] processes =
        [
            new(1, "daemon-a.exe", string.Empty, string.Empty),
            new(2, "daemon-b.exe", string.Empty, string.Empty),
        ];

        var result = _matcher.MatchRunningProcess(processes, [first, second, foregroundOnly]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, app => app.Id == first.Id);
        Assert.Contains(result, app => app.Id == second.Id);
        Assert.DoesNotContain(result, app => app.Id == foregroundOnly.Id);
    }

    [Fact]
    public void MatchRunningProcessReturnsEmptyWhenNoProcessMatches()
    {
        var app = new TrackedApp
        {
            Name = "Daemon",
            ProcessName = "daemon.exe",
            TrackingMode = TrackingMode.Running,
        };

        var result = _matcher.MatchRunningProcess([], [app]);

        Assert.Empty(result);
    }
}
