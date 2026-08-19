using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppUsageTracker.Models;
using Drawing = System.Drawing;

namespace AppUsageTracker.Services;

/// <summary>
/// 软件图标解析。优先使用软件条目里已保存的 Base64 图标，
/// 缺失时按可执行文件路径现场提取，并按路径缓存，避免每秒刷新反复读盘。
/// </summary>
public static class AppIconProvider
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>解析软件图标；取不到时返回 <c>null</c>，由界面回退到首字母色块。</summary>
    public static ImageSource? Resolve(TrackedApp? app)
    {
        if (app is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(app.IconBase64))
        {
            var key = "base64:" + app.Id.ToString("N");
            return Cache.GetOrAdd(key, _ => FromBase64(app.IconBase64));
        }

        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            return null;
        }

        return Cache.GetOrAdd(app.ExecutablePath, FromExecutable);
    }

    private static ImageSource? FromBase64(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"解析内置图标失败：{exception.Message}");
            return null;
        }
    }

    private static ImageSource? FromExecutable(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var icon = Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"提取 {path} 的图标失败：{exception.Message}");
            return null;
        }
    }
}
