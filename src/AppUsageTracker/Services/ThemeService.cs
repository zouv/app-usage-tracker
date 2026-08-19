using System.Windows;
using Microsoft.Win32;

namespace AppUsageTracker.Services;

/// <summary>
/// 主题服务。通过整体替换应用资源里的调色板字典实现浅色/深色切换，
/// 控件样式一侧使用 DynamicResource 引用，因此切换后无需重建窗口即可生效。
/// </summary>
public static class ThemeService
{
    private const string LightPalette = "Themes/Palette.Light.xaml";
    private const string DarkPalette = "Themes/Palette.Dark.xaml";

    /// <summary>当前是否处于深色模式，供托盘等非 XAML 场景取用。</summary>
    public static bool IsDarkMode { get; private set; }

    /// <summary>应用主题；<paramref name="theme"/> 取值为 System、Light 或 Dark。</summary>
    public static void Apply(string theme)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        var dark = ResolveIsDark(theme);
        IsDarkMode = dark;

        var source = new Uri(
            dark ? DarkPalette : LightPalette,
            UriKind.Relative);
        var palette = (ResourceDictionary)Application.LoadComponent(source);

        // 调色板固定占据合并字典的第 0 项，整体替换后 DynamicResource 会自动重新求值。
        var dictionaries = resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(palette);
        }
        else
        {
            dictionaries[0] = palette;
        }
    }

    private static bool ResolveIsDark(string theme) =>
        theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ||
        (theme.Equals("System", StringComparison.OrdinalIgnoreCase) && SystemUsesDarkMode());

    private static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"读取系统主题失败，回退为浅色：{exception.Message}");
            return false;
        }
    }
}
