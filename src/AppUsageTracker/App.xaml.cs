using System.Windows;
using System.IO;
using AppUsageTracker.Services;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker;

public partial class App : Application
{
    private AppRuntime? _runtime;
    private TrayIconService? _trayIcon;
    private UsageNotificationService? _notifications;
    private GlobalHotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;
    private bool _isExiting;
    private bool _hotkeyCaptureActive;

    public new static App Current => (App)Application.Current;

    public bool IsExiting => _isExiting;

    public AppRuntime Runtime =>
        _runtime ?? throw new InvalidOperationException("应用尚未完成初始化。");

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        try
        {
            AppLogger.Info("应用开始初始化。");
            var pathProvider = new AppDataPathProvider();
            var store = new JsonAppDataStore(pathProvider);
            var timeProvider = new SystemTimeProvider();
            _runtime = new AppRuntime(
                store,
                timeProvider,
                new ApplicationMatcher(),
                new ProcessScanner());
            await _runtime.InitializeAsync();
            ThemeService.Apply(_runtime.Settings.Theme);

            var overview = new OverviewViewModel(_runtime);
            var apps = new AppsViewModel(_runtime);
            var statistics = new StatisticsViewModel(_runtime);
            var editor = new SessionEditor(_runtime, timeProvider);
            var timeline = new TimelineViewModel(_runtime, editor, new CsvExportService());
            var settings = new SettingsViewModel(_runtime, new StartupService(), store);
            var mainViewModel = new MainViewModel(
                _runtime,
                overview,
                apps,
                statistics,
                timeline,
                settings);

            _mainWindow = new MainWindow { DataContext = mainViewModel };
            MainWindow = _mainWindow;
            _trayIcon = new TrayIconService(
                ShowMainWindow,
                () => _runtime.ActivityService.SetPausedAsync(!_runtime.Snapshot.IsPaused),
                () => _runtime.ActivityService.SetPrivateModeAsync(!_runtime.Snapshot.IsPrivateMode),
                AddCurrentAppAsync,
                ExitApplication);
            _runtime.SnapshotChanged += (_, snapshot) => _trayIcon.Update(snapshot);
            _trayIcon.Update(_runtime.Snapshot);
            _notifications = new UsageNotificationService(_runtime, _trayIcon);

            _hotkeyService = new GlobalHotkeyService(_mainWindow);
            _hotkeyService.Pressed += ToggleMainWindow;
            ApplyHotkey();

            var background = eventArgs.Args.Contains(
                "--background",
                StringComparer.OrdinalIgnoreCase);
            if (!background && !_runtime.Settings.StartMinimized)
            {
                _mainWindow.Show();
            }
            MemoryUsageOptimizer.ScheduleTrim();
            AppLogger.Info("应用初始化完成。");
        }
        catch (Exception exception)
        {
            AppLogger.Error("应用初始化失败。", exception);
            var errorDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AppUsageTracker");
            Directory.CreateDirectory(errorDirectory);
            File.WriteAllText(Path.Combine(errorDirectory, "startup-error.log"), exception.ToString());
            MessageBox.Show(
                $"应用初始化失败：{exception.Message}",
                "软件使用时长统计",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>切换主窗口的前台/后台：可见则隐藏到托盘，不可见则呼出并激活。</summary>
    public void ToggleMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
            MemoryUsageOptimizer.TrimWorkingSet();
        }
        else
        {
            ShowMainWindow();
        }
    }

    /// <summary>按当前设置重新注册全局快捷键，设置变更后调用。失败只降级，不阻断启动。</summary>
    public void ApplyHotkey()
    {
        // 捕捉新快捷键期间不注册，避免按下当前组合键时误触发呼出/隐藏。
        if (_hotkeyCaptureActive || _runtime is null || _hotkeyService is null)
        {
            return;
        }

        try
        {
            var hotkey = Models.HotkeyDefinition.Parse(_runtime.Settings.Hotkey);
            if (!_hotkeyService.Register(hotkey))
            {
                AppLogger.Debug($"全局快捷键注册失败：{_runtime.Settings.Hotkey}");
            }
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"全局快捷键注册异常：{exception.Message}");
        }
    }

    /// <summary>开始捕捉新快捷键：临时注销全局快捷键，聚焦结束后用 <see cref="EndHotkeyCapture"/> 恢复。</summary>
    public void BeginHotkeyCapture()
    {
        _hotkeyCaptureActive = true;
        _hotkeyService?.Unregister();
    }

    /// <summary>结束捕捉：按当前设置重新注册全局快捷键。</summary>
    public void EndHotkeyCapture()
    {
        _hotkeyCaptureActive = false;
        ApplyHotkey();
    }

    public async void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _hotkeyService?.Dispose();
        _mainWindow?.Close();
        _notifications?.Dispose();
        _trayIcon?.Dispose();
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
        }

        Shutdown();
    }

    private async Task AddCurrentAppAsync()
    {
        if (_runtime?.Snapshot.CurrentWindow is not { } window ||
            _runtime.Matcher.Match(window, _runtime.Apps) is not null)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(window.WindowTitle)
            ? Path.GetFileNameWithoutExtension(window.ProcessName)
            : window.WindowTitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries).Last();
        _runtime.Apps.Add(new Models.TrackedApp
        {
            Name = name,
            ProcessName = window.ProcessName,
            ExecutablePath = window.ExecutablePath,
        });
        await _runtime.SaveAppsAsync();
        await _runtime.ActivityService.HandleForegroundWindowAsync(window);
    }
}
