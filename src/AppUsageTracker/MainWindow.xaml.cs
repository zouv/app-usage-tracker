using System.ComponentModel;
using System.Windows;

namespace AppUsageTracker;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (!App.Current.IsExiting && App.Current.Runtime.Settings.MinimizeToTrayOnClose)
        {
            eventArgs.Cancel = true;
            Hide();
            Services.MemoryUsageOptimizer.TrimWorkingSet();
            return;
        }

        base.OnClosing(eventArgs);
    }
}
