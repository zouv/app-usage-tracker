using System.Windows;
using System.Windows.Input;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Views;

/// <summary>桌面置顶倒计时面板：仅承担拖拽与初始位置等窗口交互，状态全部来自 TimerViewModel。</summary>
public partial class CountdownPanelWindow : Window
{
    public CountdownPanelWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TimerViewModel vm)
        {
            return;
        }

        // 用设置宽度而非 ActualWidth：Loaded 时 SizeToContent 布局可能尚未完成。
        var width = vm.PanelWidth;
        var workArea = SystemParameters.WorkArea;
        var left = vm.SavedPanelLeft ?? workArea.Right - width - 24;
        var top = vm.SavedPanelTop ?? workArea.Top + 24;
        Left = Math.Clamp(
            left,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - width);
        Top = Math.Clamp(
            top,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        if (DataContext is TimerViewModel vm)
        {
            vm.SavePanelPosition(Left, Top);
        }
    }
}