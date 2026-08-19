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
}
