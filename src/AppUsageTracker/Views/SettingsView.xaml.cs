using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppUsageTracker.Models;

namespace AppUsageTracker.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 捕捉期间注销全局快捷键，避免按下当前组合键时窗口被突然隐藏。
        App.Current.BeginHotkeyCapture();
        HotkeyHint.Visibility = string.IsNullOrEmpty(HotkeyBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyHint.Visibility = Visibility.Collapsed;
        App.Current.EndHotkeyCapture();
    }

    /// <summary>
    /// 捕获组合键并回写为快捷键字符串。修饰键单独按下时不处理，等待后续主键；
    /// Esc 清空以停用快捷键。
    /// </summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
        {
            return;
        }

        if (key == Key.Escape)
        {
            if (sender is TextBox box)
            {
                box.Text = string.Empty;
                HotkeyHint.Visibility = Visibility.Visible;
            }

            return;
        }

        var modifiers = HotkeyModifiers.None;
        var keyboard = e.KeyboardDevice.Modifiers;
        if (keyboard.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (keyboard.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (keyboard.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (keyboard.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        var definition = HotkeyDefinition.FromKey(KeyInterop.VirtualKeyFromKey(key), modifiers);
        if (definition.IsValid && sender is TextBox input)
        {
            input.Text = definition.ToString();
            HotkeyHint.Visibility = Visibility.Collapsed;
        }
    }
}
