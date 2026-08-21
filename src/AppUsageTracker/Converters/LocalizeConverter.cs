using System.Globalization;
using System.Windows.Data;
using AppUsageTracker.Models;
using AppUsageTracker.Services;

namespace AppUsageTracker.Converters;

/// <summary>
/// 把持久化的中文分类值或统计模式枚举转换成当前界面语言的显示名，
/// 供软件管理列表等直接绑定模型字段的列使用。
/// </summary>
public sealed class LocalizeConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value switch
        {
            TrackingMode mode => LocalizationService.TrackingModeLabel(mode),
            string text => LocalizationService.CategoryLabel(text),
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}
