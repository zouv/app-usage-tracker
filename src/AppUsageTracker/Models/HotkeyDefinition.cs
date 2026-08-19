namespace AppUsageTracker.Models;

/// <summary>全局快捷键的组合键修饰符，对应 Win32 RegisterHotKey 的 MOD_* 位。</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// 全局快捷键定义。持久化形式是字符串（如 <c>Ctrl+Alt+T</c>），
/// 这里负责字符串与「修饰符 + 虚拟键码」之间的双向转换，供注册与界面回显复用。
/// </summary>
public sealed class HotkeyDefinition
{
    /// <summary>键名到虚拟键码的映射，覆盖字母、数字和 F1~F12，足以满足常规组合键。</summary>
    private static readonly IReadOnlyDictionary<string, int> VirtualKeyByName = BuildVirtualKeyMap();

    private static readonly IReadOnlyDictionary<int, string> NameByVirtualKey =
        VirtualKeyByName.ToDictionary(pair => pair.Value, pair => pair.Key);

    private HotkeyDefinition(int virtualKey, HotkeyModifiers modifiers, string keyName)
    {
        VirtualKey = virtualKey;
        Modifiers = modifiers;
        KeyName = keyName;
    }

    /// <summary>未配置或无法解析的组合键。</summary>
    public static HotkeyDefinition None { get; } = new(0, HotkeyModifiers.None, string.Empty);

    /// <summary>Win32 虚拟键码，见 VK_* 常量。</summary>
    public int VirtualKey { get; }

    public HotkeyModifiers Modifiers { get; }

    /// <summary>组合键里的主键名，例如 <c>T</c> 或 <c>F5</c>。</summary>
    public string KeyName { get; }

    /// <summary>是否可注册：必须有主键名且至少带一个修饰键，避免误吞单键输入。</summary>
    public bool IsValid => VirtualKey != 0 && KeyName.Length > 0 && Modifiers != HotkeyModifiers.None;

    public override string ToString()
    {
        if (!IsValid)
        {
            return string.Empty;
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    /// <summary>把持久化的字符串解析成定义；无法识别时返回 <see cref="None"/>。</summary>
    public static HotkeyDefinition Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return None;
        }

        var modifiers = HotkeyModifiers.None;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            modifiers |= tokens[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
        }

        return FromKey(ResolveVirtualKey(tokens[^1]), modifiers);
    }

    /// <summary>由虚拟键码和修饰符构造定义，主键名按映射表解析。</summary>
    public static HotkeyDefinition FromKey(int virtualKey, HotkeyModifiers modifiers) =>
        new(
            virtualKey,
            modifiers,
            NameByVirtualKey.GetValueOrDefault(virtualKey, string.Empty));

    private static int ResolveVirtualKey(string keyName) =>
        VirtualKeyByName.GetValueOrDefault(keyName, 0);

    private static Dictionary<string, int> BuildVirtualKeyMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var ch = 'A'; ch <= 'Z'; ch++)
        {
            map[ch.ToString()] = ch;
        }

        for (var ch = '0'; ch <= '9'; ch++)
        {
            map[ch.ToString()] = ch;
        }

        for (var index = 1; index <= 12; index++)
        {
            map[$"F{index}"] = 0x70 + index - 1;
        }

        return map;
    }
}
