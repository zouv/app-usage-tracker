using System.IO;
using System.Text.RegularExpressions;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

public sealed class ApplicationMatcher : IApplicationMatcher
{
    public TrackedApp? Match(
        ForegroundWindowInfo window,
        IReadOnlyCollection<TrackedApp> apps)
    {
        return apps
            .Where(app => app.Enabled)
            .Select(app => new { App = app, Score = GetScore(app, window) })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.App.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.App)
            .FirstOrDefault();
    }

    /// <summary>从正在运行的进程集合里匹配「运行」统计模式的软件（忽略窗口标题类规则）。</summary>
    public TrackedApp? MatchRunningProcess(
        IReadOnlyCollection<RunningProcessInfo> processes,
        IReadOnlyCollection<TrackedApp> apps)
    {
        if (processes.Count == 0)
        {
            return null;
        }

        return apps
            .Where(app => app.Enabled && app.TrackingMode == TrackingMode.Running)
            .Select(app => new
            {
                App = app,
                Score = processes.Max(process => GetProcessScore(app, process)),
            })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.App.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.App)
            .FirstOrDefault();
    }

    private static int GetScore(TrackedApp app, ForegroundWindowInfo window)
    {
        if (app.MatchRules.Any(rule =>
                rule.Type == MatchRuleType.ExcludeWindowTitleContains &&
                Contains(window.WindowTitle, rule.Pattern)))
        {
            return -1;
        }

        var score = -1;
        if (PathsEqual(app.ExecutablePath, window.ExecutablePath))
        {
            score = 1000;
        }

        if (ProcessNamesEqual(app.ProcessName, window.ProcessName) ||
            app.RelatedProcessNames.Any(name => ProcessNamesEqual(name, window.ProcessName)))
        {
            score = Math.Max(score, 700);
        }

        foreach (var rule in app.MatchRules)
        {
            var matched = rule.Type switch
            {
                MatchRuleType.ExecutablePath => PathsEqual(rule.Pattern, window.ExecutablePath),
                MatchRuleType.ProcessName => ProcessNamesEqual(rule.Pattern, window.ProcessName),
                MatchRuleType.WindowTitleContains => Contains(window.WindowTitle, rule.Pattern),
                MatchRuleType.WindowTitleRegex => RegexMatches(window.WindowTitle, rule.Pattern),
                MatchRuleType.ExcludeWindowTitleContains => false,
                _ => false,
            };

            if (matched)
            {
                score = Math.Max(score, 500 + rule.Priority);
            }
        }

        return score;
    }

    /// <summary>进程级打分：只看可执行路径、进程名和进程类规则，窗口标题类规则不参与。</summary>
    private static int GetProcessScore(TrackedApp app, RunningProcessInfo process)
    {
        var score = -1;
        if (PathsEqual(app.ExecutablePath, process.ExecutablePath))
        {
            score = 1000;
        }

        if (ProcessNamesEqual(app.ProcessName, process.ProcessName) ||
            app.RelatedProcessNames.Any(name => ProcessNamesEqual(name, process.ProcessName)))
        {
            score = Math.Max(score, 700);
        }

        foreach (var rule in app.MatchRules)
        {
            var matched = rule.Type switch
            {
                MatchRuleType.ExecutablePath => PathsEqual(rule.Pattern, process.ExecutablePath),
                MatchRuleType.ProcessName => ProcessNamesEqual(rule.Pattern, process.ProcessName),
                _ => false,
            };

            if (matched)
            {
                score = Math.Max(score, 500 + rule.Priority);
            }
        }

        return score;
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool ProcessNamesEqual(string left, string right) =>
        string.Equals(
            Path.GetFileNameWithoutExtension(left?.Trim()),
            Path.GetFileNameWithoutExtension(right?.Trim()),
            StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string value, string pattern) =>
        !string.IsNullOrWhiteSpace(pattern) &&
        value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    private static bool RegexMatches(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
