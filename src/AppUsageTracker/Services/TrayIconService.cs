using AppUsageTracker.Models;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AppUsageTracker.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly Action _showWindow;
    private readonly Func<Task> _togglePause;
    private readonly Func<Task> _togglePrivateMode;
    private readonly Func<Task> _addCurrentApp;
    private readonly Action _exit;

    public TrayIconService(
        Action showWindow,
        Func<Task> togglePause,
        Func<Task> togglePrivateMode,
        Func<Task> addCurrentApp,
        Action exit)
    {
        _showWindow = showWindow;
        _togglePause = togglePause;
        _togglePrivateMode = togglePrivateMode;
        _addCurrentApp = addCurrentApp;
        _exit = exit;
        _statusItem = new WinForms.ToolStripMenuItem("监听运行中") { Enabled = false };
        _pauseItem = new WinForms.ToolStripMenuItem("暂停监听");
        _pauseItem.Click += async (_, _) => await _togglePause();
        var privateItem = new WinForms.ToolStripMenuItem("切换隐私模式");
        privateItem.Click += async (_, _) => await _togglePrivateMode();
        var addItem = new WinForms.ToolStripMenuItem("添加当前软件");
        addItem.Click += async (_, _) => await _addCurrentApp();
        var showItem = new WinForms.ToolStripMenuItem("显示主窗口");
        showItem.Click += (_, _) => _showWindow();
        var exitItem = new WinForms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => _exit();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new WinForms.ToolStripSeparator(),
            showItem,
            _pauseItem,
            addItem,
            privateItem,
            new WinForms.ToolStripSeparator(),
            exitItem,
        ]);
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "软件使用时长统计",
            Icon = LoadApplicationIcon(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _showWindow();
    }

    /// <summary>读取随程序集嵌入的应用图标；失败时回退到系统图标，不阻断托盘创建。</summary>
    private static Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("Assets/app.ico", UriKind.Relative));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream, new Drawing.Size(32, 32));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"加载托盘图标失败，回退为系统图标：{exception.Message}");
        }

        return Drawing.SystemIcons.Application;
    }

    public void Update(TrackingSnapshot snapshot)
    {
        var app = snapshot.CurrentApp?.Name ?? StateText(snapshot.State);
        var duration = snapshot.CurrentSession is null
            ? string.Empty
            : $" {ViewModels.DurationFormatter.FormatClock(snapshot.CurrentSession.DurationSeconds)}";
        var text = $"时迹 - {app}{duration}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
        _statusItem.Text = app;
        _pauseItem.Text = snapshot.IsPaused ? "继续监听" : "暂停监听";
    }

    public void ShowNotification(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static string StateText(ActivityState state) =>
        state switch
        {
            ActivityState.Active => "监听运行中",
            ActivityState.Paused => "监听已暂停",
            ActivityState.Private => "隐私模式",
            ActivityState.Idle => "用户空闲",
            ActivityState.Locked => "系统锁屏",
            _ => state.ToString(),
        };
}
