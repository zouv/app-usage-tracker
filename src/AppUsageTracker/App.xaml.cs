using System.Windows;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using AppUsageTracker.Services;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "AppUsageTracker.SingleInstance";
    private const string ShowInstanceEventName = "AppUsageTracker.ShowInstance";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showInstanceSignal;
    private Thread? _showSignalThread;
    private AppRuntime? _runtime;
    private TrayIconService? _trayIcon;
    private UsageNotificationService? _notifications;
    private GlobalHotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;
    private bool _isExiting;
    private bool _hotkeyCaptureActive;
    private bool _trayHintShown;

    public event Action<bool, string>? HotkeyRegistrationChanged;

    public new static App Current => (App)Application.Current;

    public bool IsExiting => _isExiting;

    public AppRuntime Runtime =>
        _runtime ?? throw new InvalidOperationException("应用尚未完成初始化。");

    public AppRuntime? RuntimeOrNull => _runtime;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        RegisterCrashLogging();
        SessionEnding += OnSessionEnding;

        // 单实例：已有一个实例运行时，唤醒它并退出本实例，避免双实例争抢数据文件。
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showInstanceSignal = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ShowInstanceEventName);
        _showSignalThread = new Thread(ShowInstanceSignalLoop) { IsBackground = true };
        _showSignalThread.Start();

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
            LocalizationService.Apply(_runtime.Settings.Language);
            ApplyAutoStart(_runtime.Settings.AutoStart);

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
                LocalizationService.T("Loc.App.InitFailed", exception.Message),
                LocalizationService.T("Loc.App.InitFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>通知已运行的首实例呼出主窗口；首实例尚未就绪时静默忽略。</summary>
    private static void SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowInstanceEventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    /// <summary>后台监听唤醒信号，收到后切回 UI 线程呼出主窗口。</summary>
    private void ShowInstanceSignalLoop()
    {
        while (true)
        {
            try
            {
                var signal = _showInstanceSignal;
                if (signal is null)
                {
                    return;
                }

                signal.WaitOne();
                if (_isExiting)
                {
                    return;
                }

                Dispatcher.Invoke(ShowMainWindow);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                AppLogger.Debug($"单实例唤醒线程异常：{exception.Message}");
                return;
            }
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
        // 窗口隐藏期间页面暂停了重算，重新显示后立即补一次数据。
        _runtime?.NotifyDataChanged();
    }

    /// <summary>隐藏到托盘；每次运行首次隐藏时说明应用仍在后台监听。</summary>
    public void HideMainWindowToTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Hide();
        MemoryUsageOptimizer.ScheduleTrim();
        if (_trayHintShown || _trayIcon is null)
        {
            return;
        }

        _trayHintShown = true;
        _trayIcon.ShowNotification(
            LocalizationService.T("Loc.Tray.HintTitle"),
            LocalizationService.T("Loc.Tray.HintMessage"));
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
            HideMainWindowToTray();
        }
        else
        {
            ShowMainWindow();
        }
    }

    /// <summary>把运行中的未处理异常写入日志，便于常驻后台时定位闪退。</summary>
    private void RegisterCrashLogging()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
            AppLogger.Error("界面线程未处理异常。", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                AppLogger.Error("未处理的后台异常。", exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("未观察的任务异常。", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    /// <summary>
    /// 修正已存在的开机启动项路径（便携版移动目录后仍能启动）；
    /// 首次运行且设置里为默认开启时不主动写入注册表，避免未确认就注册自启动。
    /// </summary>
    private static void ApplyAutoStart(bool enabled)
    {
        try
        {
            var startup = new StartupService();
            if (enabled || startup.IsEnabled())
            {
                startup.SetEnabled(enabled);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"更新开机启动设置失败：{exception.Message}");
        }
    }

    /// <summary>按当前设置重新注册全局快捷键。空快捷键表示主动停用，不视为失败。</summary>
    public bool ApplyHotkey()
    {
        // 捕捉新快捷键期间不注册，避免按下当前组合键时误触发呼出/隐藏。
        if (_hotkeyCaptureActive || _runtime is null || _hotkeyService is null)
        {
            return true;
        }

        var hotkeyText = _runtime.Settings.Hotkey;
        var succeeded = false;
        try
        {
            if (string.IsNullOrWhiteSpace(hotkeyText))
            {
                _hotkeyService.Unregister();
                succeeded = true;
            }
            else
            {
                var hotkey = Models.HotkeyDefinition.Parse(hotkeyText);
                succeeded = _hotkeyService.Register(hotkey);
                if (!succeeded)
                {
                    AppLogger.Debug($"全局快捷键注册失败：{hotkeyText}");
                }
            }
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"全局快捷键注册异常：{exception.Message}");
        }

        HotkeyRegistrationChanged?.Invoke(succeeded, hotkeyText);
        return succeeded;
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

    /// <summary>Windows 注销/关机前同步等待一次完整收尾，避免只发起异步退出就被系统终止。</summary>
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs eventArgs)
    {
        if (_isExiting || _runtime is null)
        {
            return;
        }

        _isExiting = true;
        try
        {
            _hotkeyService?.Dispose();
            if (!_runtime.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)))
            {
                AppLogger.Debug("关机前的数据保存超过 5 秒，系统将按默认流程继续结束会话。");
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("关机或注销前保存数据失败。", exception);
        }
    }

    public async void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        SignalShutdown();
        try
        {
            _hotkeyService?.Dispose();
            _mainWindow?.Close();
            _notifications?.Dispose();
            _trayIcon?.Dispose();
            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            // 保存失败也必须退出，避免应用卡在无窗口状态。
            AppLogger.Error("退出应用时发生异常。", exception);
        }
        finally
        {
            ReleaseSingleInstance();
            Shutdown();
        }
    }

    /// <summary>
    /// 导入备份后立即退出，不再写回旧的内存数据。调用方应确保导入已经完成，
    /// 下次启动时由正常加载流程读取导入后的文件。
    /// </summary>
    public void ExitWithoutSaving()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        SignalShutdown();
        try
        {
            _hotkeyService?.Dispose();
            _mainWindow?.Close();
            _notifications?.Dispose();
            _trayIcon?.Dispose();
        }
        catch (Exception exception)
        {
            AppLogger.Error("退出应用时发生异常。", exception);
        }
        finally
        {
            ReleaseSingleInstance();
            Shutdown();
        }
    }

    /// <summary>通知单实例唤醒线程退出，但不提前释放互斥体，避免保存收尾期间第二个实例启动。</summary>
    private void SignalShutdown()
    {
        _showInstanceSignal?.Set();
        _showInstanceSignal?.Dispose();
        _showInstanceSignal = null;
    }

    private void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex is null)
        {
            return;
        }

        try
        {
            _singleInstanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 互斥体未被本线程持有，忽略。
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
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
    }
}
