using System.Windows;
using System.Windows.Controls;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Views;

public partial class StatisticsView : UserControl
{
    public StatisticsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is StatisticsViewModel viewModel)
        {
            viewModel.Activate();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is StatisticsViewModel viewModel)
        {
            viewModel.Deactivate();
        }
    }
}
