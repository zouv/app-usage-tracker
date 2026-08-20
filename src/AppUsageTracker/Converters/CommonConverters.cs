using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace AppUsageTracker.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is not Visibility.Visible;
}

public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is not null &&
        string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        // 仅在被选中（true）时回写，避免 RadioButton 组取消勾选时覆盖已选周期。
        if (value is not true || targetType is null)
        {
            return Binding.DoNothing;
        }

        try
        {
            return Enum.Parse(targetType, parameter?.ToString() ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return Binding.DoNothing;
        }
    }
}

public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        try
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(value?.ToString() ?? "#7A8491"));
        }
        catch
        {
            return Brushes.SlateGray;
        }
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NonZeroCountVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class ZeroCountVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class Base64ImageConverter : IValueConverter
{
    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not string base64 || string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = System.Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}
