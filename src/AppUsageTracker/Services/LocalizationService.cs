using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

/// <summary>
/// 界面语言服务。通过整体替换应用资源里的字符串字典实现中英文切换，
/// XAML 一侧使用 DynamicResource 引用，因此切换后无需重建窗口即可生效；
/// 代码一侧使用 <see cref="T"/> 按当前语言取文案。
/// 文案的唯一来源是 Strings/*.xml，以嵌入式资源打包，
/// 因此 <see cref="T"/> 在没有 WPF Application 的环境（单元测试）里也能工作。
/// </summary>
public static class LocalizationService
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private const string ChineseDictionary = "Strings/Strings.Chinese.xml";
    private const string EnglishDictionary = "Strings/Strings.English.xml";

    /// <summary>语言变化时触发（在 UI 线程同步触发），订阅方按需重算动态文案。</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>当前界面语言，取值 zh-CN 或 en-US，默认中文。</summary>
    public static string Current { get; private set; } = Chinese;

    private static IReadOnlyDictionary<string, string> _strings =
        LoadEmbeddedDictionary(Chinese);

    /// <summary>字符串字典是否已插入应用资源合并字典（第 1 项）。</summary>
    private static bool _installed;

    /// <summary>是否处于英文界面，供代码里的日期与时长格式分支取用。</summary>
    public static bool IsEnglish => Current == English;

    /// <summary>规范化语言标识；未知值一律回退中文。</summary>
    public static string Normalize(string language) =>
        language == English ? English : Chinese;

    /// <summary>应用界面语言；<paramref name="language"/> 取值为 zh-CN 或 en-US。</summary>
    public static void Apply(string language)
    {
        language = Normalize(language);
        var changed = language != Current;
        Current = language;
        if (changed)
        {
            _strings = LoadEmbeddedDictionary(language);
        }

        if (Application.Current?.Resources is { } resources)
        {
            var strings = new ResourceDictionary();
            foreach (var pair in _strings)
            {
                strings[pair.Key] = pair.Value;
            }

            // 字符串字典固定占据合并字典的第 1 项（第 0 项是调色板，由 ThemeService 管理），
            // 整体替换后 DynamicResource 会自动重新求值。
            var dictionaries = resources.MergedDictionaries;
            if (_installed && dictionaries.Count > 1)
            {
                dictionaries[1] = strings;
            }
            else
            {
                dictionaries.Insert(Math.Min(1, dictionaries.Count), strings);
                _installed = true;
            }
        }

        if (changed)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>按当前语言取文案；找不到键时回退键名，避免界面出现空文本。</summary>
    public static string T(string key, params object[] args)
    {
        var value = _strings.GetValueOrDefault(key, key);
        return args.Length == 0 ? value : string.Format(value, args);
    }

    /// <summary>统计模式的本地化显示名。</summary>
    public static string TrackingModeLabel(TrackingMode mode) => T(mode switch
    {
        TrackingMode.Foreground => "Loc.Apps.Mode.Foreground",
        TrackingMode.Running => "Loc.Apps.Mode.Running",
        _ => "Loc.Apps.Mode.Effective",
    });

    /// <summary>会话结束原因的本地化显示名。</summary>
    public static string EndReasonLabel(SessionEndReason reason) => T(reason switch
    {
        SessionEndReason.WindowChanged => "Loc.Session.Reason.WindowChanged",
        SessionEndReason.Idle => "Loc.Session.Reason.Idle",
        SessionEndReason.Locked => "Loc.Session.Reason.Locked",
        SessionEndReason.Suspended => "Loc.Session.Reason.Suspended",
        SessionEndReason.Paused => "Loc.Session.Reason.Paused",
        SessionEndReason.PrivateMode => "Loc.Session.Reason.PrivateMode",
        SessionEndReason.Disabled => "Loc.Session.Reason.Disabled",
        SessionEndReason.Midnight => "Loc.Session.Reason.Midnight",
        SessionEndReason.ApplicationExit => "Loc.Session.Reason.ApplicationExit",
        SessionEndReason.Recovered => "Loc.Session.Reason.Recovered",
        SessionEndReason.Manual => "Loc.Session.Reason.Manual",
        _ => "Loc.Session.Reason.None",
    });

    /// <summary>活动状态的本地化显示名。</summary>
    public static string ActivityStateLabel(ActivityState state) => T(state switch
    {
        ActivityState.Active => "Loc.State.Active",
        ActivityState.Idle => "Loc.State.Idle",
        ActivityState.Locked => "Loc.State.Locked",
        ActivityState.Suspended => "Loc.State.Suspended",
        ActivityState.Paused => "Loc.State.Paused",
        ActivityState.Private => "Loc.State.Private",
        ActivityState.Untracked => "Loc.State.Untracked",
        _ => "Loc.State.Stopped",
    });

    /// <summary>软件分类的本地化显示名；分类以中文值持久化，未收录的值原样返回。</summary>
    public static string CategoryLabel(string canonical) => T(canonical switch
    {
        "未分类" => "Loc.Apps.Category.Uncategorized",
        "开发工具" => "Loc.Apps.Category.Development",
        "浏览器" => "Loc.Apps.Category.Browser",
        "游戏" => "Loc.Apps.Category.Game",
        "通讯" => "Loc.Apps.Category.Communication",
        "影音" => "Loc.Apps.Category.Media",
        "办公" => "Loc.Apps.Category.Office",
        _ => canonical,
    });

    /// <summary>完整日期：中文「2026年8月21日」，英文「Aug 21, 2026」。</summary>
    public static string FullDate(DateTime value) => IsEnglish
        ? value.ToString("MMM d, yyyy", EnglishCulture)
        : value.ToString("yyyy年M月d日");

    /// <summary>带星期的完整日期，用于图表悬浮提示。</summary>
    public static string FullDateWithWeekday(DateTime value) => IsEnglish
        ? value.ToString("ddd, MMM d, yyyy", EnglishCulture)
        : value.ToString("yyyy年M月d日 ddd");

    /// <summary>年月：中文「2026年8月」，英文「Aug 2026」。</summary>
    public static string MonthYear(DateTime value) => IsEnglish
        ? value.ToString("MMM yyyy", EnglishCulture)
        : value.ToString("yyyy年M月");

    /// <summary>周起点说明：中文「2026年8月17日 周」，英文「Week of Aug 17, 2026」。</summary>
    public static string WeekStartLabel(DateTime value) => IsEnglish
        ? $"Week of {FullDate(value)}"
        : $"{FullDate(value)} 周";

    /// <summary>年份。</summary>
    public static string Year(DateTime value) => value.ToString("yyyy");

    private static CultureInfo EnglishCulture => CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// 从随程序集嵌入的字符串字典里解析键值对，供 <see cref="T"/> 与界面字符串资源字典取用。
    /// 失败时返回空字典，界面文案回退为键名，不阻断应用。
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadEmbeddedDictionary(string language)
    {
        // 清单资源名把目录分隔符折叠成点：Strings/Strings.Chinese.xml → AppUsageTracker.Strings.Strings.Chinese.xml
        var resourceName = "AppUsageTracker." +
                           (language == English ? EnglishDictionary : ChineseDictionary)
                           .Replace('/', '.');
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return new Dictionary<string, string>();
            }

            var document = XDocument.Load(stream);
            var result = new Dictionary<string, string>();
            foreach (var element in document.Descendants())
            {
                if (element.Name.LocalName == "String" &&
                    element.Attribute("Key") is { } keyAttribute)
                {
                    result[keyAttribute.Value] = element.Value;
                }
            }

            return result;
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"加载嵌入字符串字典失败：{exception.Message}");
            return new Dictionary<string, string>();
        }
    }
}
