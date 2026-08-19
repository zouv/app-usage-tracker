using System.Windows;
using System.Windows.Controls;
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
}
