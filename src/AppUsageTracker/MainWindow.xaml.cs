using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AppUsageTracker;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Windows 11 上把窗口圆角设为 Round（Windows 10 自动忽略）。
    /// 窗口用了 WindowStyle=None + WindowChrome，系统默认不再附带圆角，
    /// 需要显式通过 DwmSetWindowAttribute 申请。
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2
            var preference = 2;
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 不支持该属性，静默降级为直角。
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void OnStateChanged(object? sender, EventArgs eventArgs)
    {
        // 最大化时把最大化图标换成「还原」。
        var key = WindowState == WindowState.Maximized ? "Icon.Restore" : "Icon.Maximize";
        if (TryFindResource(key) is System.Windows.Media.Geometry geometry)
        {
            MaximizeIcon.Data = geometry;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

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
