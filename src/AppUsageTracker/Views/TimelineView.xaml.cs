using System.Windows;
using System.Windows.Controls;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Views;

public partial class TimelineView : UserControl
{
    public TimelineView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != System.Windows.Input.Key.Escape ||
            DataContext is not TimelineViewModel { IsEditorOpen: true } viewModel)
        {
            return;
        }

        viewModel.CancelEditorCommand.Execute(null);
        eventArgs.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is TimelineViewModel viewModel)
        {
            viewModel.Activate();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is TimelineViewModel viewModel)
        {
            viewModel.Deactivate();
        }
    }
}
