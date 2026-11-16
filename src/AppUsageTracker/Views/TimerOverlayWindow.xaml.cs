using System.Windows;
using System.Windows.Input;

namespace AppUsageTracker.Views;

/// <summary>倒计时结束的全屏置顶桌布：覆盖主屏，底板透明度绑定设置、文字不透明，仅 Esc 关闭。</summary>
public partial class TimerOverlayWindow : Window
{
    public TimerOverlayWindow()
    {
        InitializeComponent();
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}