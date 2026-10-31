using System.Windows;
using System.Windows.Controls;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is OverviewViewModel viewModel)
        {
            viewModel.Activate();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is OverviewViewModel viewModel)
        {
            viewModel.Deactivate();
        }
    }
}
