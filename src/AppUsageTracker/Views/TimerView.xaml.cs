using System.Windows.Controls;

namespace AppUsageTracker.Views;

/// <summary>定时器独立页面：状态与命令全部来自 TimerViewModel，Code-behind 不承载业务。</summary>
public partial class TimerView : UserControl
{
    public TimerView()
    {
        InitializeComponent();
    }
}