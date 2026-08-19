using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

/// <summary>
/// 软件配色槽位。8 个固定色相按顺序分配给软件，浅色与深色一一对应。
/// 该顺序已通过相邻配对的色觉障碍分离度校验，不要随意调整顺序或色值。
/// </summary>
public static class AppColorPalette
{
    /// <summary>历史默认色。等于该值或为空视为“尚未分配”，由本服务回填。</summary>
    public const string LegacyDefaultHex = "#2F6BDE";

    /// <summary>浅色主题下的槽位色，索引即槽位号。</summary>
    private static readonly string[] LightSlots =
    [
        "#2A78D6", // 蓝
        "#EB6834", // 橙
        "#1BAF7A", // 青
        "#EDA100", // 黄
        "#E87BA4", // 品红
        "#008300", // 绿
        "#4A3AA7", // 紫
        "#E34948", // 红
    ];

    /// <summary>深色主题下的槽位色，与 <see cref="LightSlots"/> 逐位对应。</summary>
    private static readonly string[] DarkSlots =
    [
        "#3987E5",
        "#D95926",
        "#199E70",
        "#C98500",
        "#D55181",
        "#008300",
        "#9085E9",
        "#E66767",
    ];

    /// <summary>未匹配到软件时使用的中性灰。</summary>
    public const string NeutralHex = "#7A8491";

    public static IReadOnlyList<string> Slots => LightSlots;

    /// <summary>
    /// 为尚未分配颜色的软件按槽位顺序回填，优先使用尚未被占用的槽位。
    /// 返回是否发生了改动，调用方据此决定是否需要持久化。
    /// </summary>
    public static bool AssignMissing(IEnumerable<TrackedApp> apps)
    {
        var list = apps.ToList();
        var usage = new int[LightSlots.Length];
        foreach (var app in list)
        {
            var slot = IndexOfSlot(app.ColorHex);
            if (slot >= 0)
            {
                usage[slot]++;
            }
        }

        var changed = false;
        foreach (var app in list)
        {
            if (!NeedsAssignment(app.ColorHex))
            {
                continue;
            }

            var slot = LeastUsedSlot(usage);
            app.ColorHex = LightSlots[slot];
            usage[slot]++;
            changed = true;
        }

        return changed;
    }

    /// <summary>把持久化的槽位色换算成当前主题下的显示色；自定义色原样返回。</summary>
    public static string Resolve(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return ThemeService.IsDarkMode ? DarkSlots[0] : LightSlots[0];
        }

        var slot = IndexOfSlot(colorHex);
        if (slot < 0)
        {
            return colorHex;
        }

        return ThemeService.IsDarkMode ? DarkSlots[slot] : LightSlots[slot];
    }

    private static bool NeedsAssignment(string? colorHex) =>
        string.IsNullOrWhiteSpace(colorHex) ||
        string.Equals(colorHex, LegacyDefaultHex, StringComparison.OrdinalIgnoreCase);

    private static int IndexOfSlot(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return -1;
        }

        for (var index = 0; index < LightSlots.Length; index++)
        {
            if (string.Equals(LightSlots[index], colorHex, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(DarkSlots[index], colorHex, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int LeastUsedSlot(IReadOnlyList<int> usage)
    {
        var best = 0;
        for (var index = 1; index < usage.Count; index++)
        {
            if (usage[index] < usage[best])
            {
                best = index;
            }
        }

        return best;
    }
}
