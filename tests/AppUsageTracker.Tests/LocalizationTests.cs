using System.Xml.Linq;
using AppUsageTracker.Services;
using AppUsageTracker.ViewModels;
using Xunit;

// 本测试集会切换 LocalizationService 的全局语言状态，
// 与其他测试类并行时会互相干扰，因此整个测试程序集串行执行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AppUsageTracker.Tests;

public sealed class LocalizationTests
{
    private const string ChineseResource = "AppUsageTracker.Strings.Strings.Chinese.xml";
    private const string EnglishResource = "AppUsageTracker.Strings.Strings.English.xml";

    [Fact]
    public void StringDictionaries_HaveIdenticalKeysAndNonEmptyValues()
    {
        var chinese = LoadDictionary(ChineseResource);
        var english = LoadDictionary(EnglishResource);

        Assert.NotEmpty(chinese);
        Assert.Equal(chinese.Keys.OrderBy(key => key), english.Keys.OrderBy(key => key));
        Assert.All(
            chinese.Concat(english),
            pair => Assert.False(
                string.IsNullOrWhiteSpace(pair.Value),
                $"{pair.Key} 的翻译为空"));
    }

    [Fact]
    public void Normalize_FallsBackToChineseForUnknownLanguage()
    {
        Assert.Equal(LocalizationService.Chinese, LocalizationService.Normalize("fr-FR"));
        Assert.Equal(LocalizationService.Chinese, LocalizationService.Normalize(""));
        Assert.Equal(LocalizationService.English, LocalizationService.Normalize("en-US"));
    }

    [Fact]
    public void DurationFormatter_UsesLocalizedUnits()
    {
        try
        {
            LocalizationService.Apply(LocalizationService.Chinese);
            Assert.Equal("1小时01分钟", DurationFormatter.Format(3661));
            Assert.Equal("5分钟05秒", DurationFormatter.Format(305));
            Assert.Equal("30秒", DurationFormatter.Format(30));

            LocalizationService.Apply(LocalizationService.English);
            Assert.Equal("1h01min", DurationFormatter.Format(3661));
            Assert.Equal("5min05s", DurationFormatter.Format(305));
            Assert.Equal("30s", DurationFormatter.Format(30));
        }
        finally
        {
            // 恢复默认语言，避免影响其他测试。
            LocalizationService.Apply(LocalizationService.Chinese);
        }
    }

    [Fact]
    public void FullDate_FormatsByLanguage()
    {
        var date = new DateTime(2026, 8, 21);
        try
        {
            LocalizationService.Apply(LocalizationService.Chinese);
            Assert.Equal("2026年8月21日", LocalizationService.FullDate(date));

            LocalizationService.Apply(LocalizationService.English);
            Assert.Equal("Aug 21, 2026", LocalizationService.FullDate(date));
        }
        finally
        {
            LocalizationService.Apply(LocalizationService.Chinese);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadDictionary(string resourceName)
    {
        // 字符串以嵌入资源形式打包在 AppUsageTracker 主程序集里，而不是测试程序集。
        using var stream = typeof(LocalizationService).Assembly
            .GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        var document = XDocument.Load(stream!);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "String")
            .ToDictionary(
                element => element.Attribute("Key")!.Value,
                element => element.Value);
    }
}
