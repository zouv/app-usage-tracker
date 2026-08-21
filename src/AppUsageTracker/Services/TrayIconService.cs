using AppUsageTracker.Models;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AppUsageTracker.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _privateItem;
    private readonly WinForms.ToolStripMenuItem _addItem;
    private readonly WinForms.ToolStripMenuItem _showItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private readonly Action _showWindow;
    private readonly Func<Task> _togglePause;
    private readonly Func<Task> _togglePrivateMode;
    private readonly Func<Task> _addCurrentApp;
    private readonly Action _exit;
    private TrackingSnapshot? _snapshot;

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
        _statusItem = new WinForms.ToolStripMenuItem(LocalizationService.T("Loc.Status.Running"))
        {
            Enabled = false,
        };
        _pauseItem = new WinForms.ToolStripMenuItem(LocalizationService.T("Loc.Tray.Pause"));
        _pauseItem.Click += async (_, _) => await _togglePause();
        _privateItem = new WinForms.ToolStripMenuItem(LocalizationService.T("Loc.Tray.Private"));
        _privateItem.Click += async (_, _) => await _togglePrivateMode();
        _addItem = new WinForms.ToolStripMenuItem(LocalizationService.T("Loc.Tray.Add"));
        _addItem.Click += async (_, _) => await _addCurrentApp();
        _showItem = new WinForms.ToolStripMenuItem(LocalizationService.T("Loc.Tray.Show"));
        _showItem.Click += (_, _) => _showWindow();
        _exitItem = new WinForms.ToolStripMenuItem(LocalizationService.T("Loc.Tray.Exit"));
        _exitItem.Click += (_, _) => _exit();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new WinForms.ToolStripSeparator(),
            _showItem,
            _pauseItem,
            _addItem,
            _privateItem,
            new WinForms.ToolStripSeparator(),
            _exitItem,
        ]);
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = LocalizationService.T("Loc.Tray.Tooltip"),
            Icon = LoadApplicationIcon(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _showWindow();
        LocalizationService.LanguageChanged += OnLanguageChanged;
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

    private void OnLanguageChanged(object? sender, EventArgs eventArgs) => ApplyTexts();

    public void Update(TrackingSnapshot snapshot)
    {
        _snapshot = snapshot;
        ApplyTexts();
    }

    /// <summary>按最近一次快照与当前界面语言刷新菜单项和悬浮文本。</summary>
    private void ApplyTexts()
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            return;
        }

        var app = snapshot.CurrentApp?.Name ?? TrayStateText(snapshot.State);
        var duration = snapshot.CurrentSession is null
            ? string.Empty
            : $" {ViewModels.DurationFormatter.FormatClock(snapshot.CurrentSession.DurationSeconds)}";
        var text = $"时迹 - {app}{duration}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
        _statusItem.Text = app;
        _pauseItem.Text = LocalizationService.T(
            snapshot.IsPaused ? "Loc.Tray.Resume" : "Loc.Tray.Pause");
        _privateItem.Text = LocalizationService.T("Loc.Tray.Private");
        _addItem.Text = LocalizationService.T("Loc.Tray.Add");
        _showItem.Text = LocalizationService.T("Loc.Tray.Show");
        _exitItem.Text = LocalizationService.T("Loc.Tray.Exit");
    }

    /// <summary>没有当前软件时的托盘状态文本，与侧边栏的监听状态口径一致。</summary>
    private static string TrayStateText(ActivityState state) => state switch
    {
        ActivityState.Paused => LocalizationService.T("Loc.Status.Paused"),
        ActivityState.Private => LocalizationService.T("Loc.Status.Private"),
        ActivityState.Stopped => LocalizationService.T("Loc.Status.Stopped"),
        ActivityState.Idle => LocalizationService.T("Loc.State.Idle"),
        ActivityState.Locked => LocalizationService.T("Loc.State.Locked"),
        ActivityState.Suspended => LocalizationService.T("Loc.State.Suspended"),
        ActivityState.Untracked => LocalizationService.T("Loc.State.Untracked"),
        _ => LocalizationService.T("Loc.Status.Running"),
    };

    public void ShowNotification(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
