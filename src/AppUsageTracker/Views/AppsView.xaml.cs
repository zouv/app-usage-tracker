using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppUsageTracker.Models;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Views;

public partial class AppsView : UserControl
{
    public AppsView()
    {
        InitializeComponent();
    }

    private void OnCloseScan(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AppsViewModel viewModel)
        {
            viewModel.RunningProcesses.Clear();
        }
    }

    /// <summary>双击软件条目直接打开编辑界面，免去先选中再点「编辑」。</summary>
    private void OnAppRowDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        if (DataContext is not AppsViewModel viewModel)
        {
            return;
        }

        // 双击空白区不响应；只有命中 DataGridRow 才打开编辑。
        if (eventArgs.OriginalSource is not DependencyObject source ||
            FindAncestor<DataGridRow>(source)?.DataContext is not TrackedApp app)
        {
            return;
        }

        viewModel.SelectedApp = app;
        if (viewModel.EditSelectedCommand.CanExecute(null))
        {
            viewModel.EditSelectedCommand.Execute(null);
        }
    }

    /// <summary>编辑弹窗打开时按 Esc 等同于「取消」。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape ||
            DataContext is not AppsViewModel viewModel ||
            !viewModel.IsEditorOpen)
        {
            return;
        }

        if (viewModel.CancelEditorCommand.CanExecute(null))
        {
            viewModel.CancelEditorCommand.Execute(null);
        }

        eventArgs.Handled = true;
    }

    /// <summary>首次加载时同步一次下拉框自身的 tooltip。</summary>
    private void OnTrackingModeComboLoaded(object sender, RoutedEventArgs eventArgs) =>
        UpdateTrackingModeTooltip();

    /// <summary>选中项变化后同步下拉框自身的 tooltip（collapsed 状态悬停也能看到说明）。</summary>
    private void OnTrackingModeChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        UpdateTrackingModeTooltip();

    private void UpdateTrackingModeTooltip()
    {
        if (TrackingModeCombo.SelectedItem is OptionItem option)
        {
            TrackingModeCombo.ToolTip = option.Tooltip;
        }
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        return null;
    }
}
