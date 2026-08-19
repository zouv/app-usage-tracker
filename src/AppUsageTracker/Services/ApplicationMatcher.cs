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
