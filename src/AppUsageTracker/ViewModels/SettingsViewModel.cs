using System.Diagnostics;
using System.IO;
using System.Windows;
using AppUsageTracker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AppUsageTracker.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppRuntime _runtime;
    private readonly StartupService _startup;
    private readonly IAppDataStore _store;
    private bool _isLoading;

    public SettingsViewModel(
        AppRuntime runtime,
        StartupService startup,
        IAppDataStore store)
    {
        _runtime = runtime;
        _startup = startup;
        _store = store;
        Load();
        if (Application.Current is App app)
        {
            app.HotkeyRegistrationChanged += OnHotkeyRegistrationChanged;
        }
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    /// <summary>语言选项，名称固定用各自语言书写（中文 / English），不随界面语言翻译。</summary>
    public IReadOnlyList<OptionItem> LanguageOptions { get; } =
    [
        new(LocalizationService.Chinese, "中文"),
        new(LocalizationService.English, "English"),
    ];

    [ObservableProperty]
    private string _language = LocalizationService.Chinese;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private string _hotkey = "Ctrl+Alt+T";

    [ObservableProperty]
    private bool _excludeIdleTime;

    [ObservableProperty]
    private int _idleThresholdMinutes;

    [ObservableProperty]
    private int _shortSwitchSeconds;

    [ObservableProperty]
    private int _heartbeatSeconds;

    [ObservableProperty]
    private int _autoSaveSeconds;

    [ObservableProperty]
    private int _maxHistoryDays;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private int _dailySummaryHour = 18;

    [ObservableProperty]
    private int _dailySummaryMinute;

    [ObservableProperty]
    private string _theme = "System";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private string _hotkeyStatusMessage = string.Empty;

    partial void OnAutoStartChanged(bool value) => ApplyIfNotLoading();
    partial void OnStartMinimizedChanged(bool value) => ApplyIfNotLoading();
    partial void OnMinimizeToTrayOnCloseChanged(bool value) => ApplyIfNotLoading();
    partial void OnHotkeyChanged(string value)
    {
        HotkeyStatusMessage = string.Empty;
        ApplyIfNotLoading();
    }
    partial void OnExcludeIdleTimeChanged(bool value) => ApplyIfNotLoading();
    partial void OnIdleThresholdMinutesChanged(int value) => ApplyIfNotLoading();
    partial void OnShortSwitchSecondsChanged(int value) => ApplyIfNotLoading();
    partial void OnAutoSaveSecondsChanged(int value) => ApplyIfNotLoading();
    partial void OnMaxHistoryDaysChanged(int value) => ApplyIfNotLoading();
    partial void OnNotificationsEnabledChanged(bool value) => ApplyIfNotLoading();
    partial void OnDailySummaryHourChanged(int value) => ApplyIfNotLoading();
    partial void OnDailySummaryMinuteChanged(int value) => ApplyIfNotLoading();
    partial void OnThemeChanged(string value) => ApplyIfNotLoading();
    partial void OnLanguageChanged(string value) => ApplyIfNotLoading();

    [RelayCommand]
    private void OpenDataDirectory()
    {
        try
        {
            var path = new AppDataPathProvider().RootDirectory;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.Error("打开数据目录失败。", exception);
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.T("Loc.Settings.ChooseBackupDir"),
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var target = await _store.CreateBackupAsync(dialog.FolderName);
                StatusMessage = LocalizationService.T("Loc.Settings.BackupSaved", target);
            }
            catch (Exception exception)
            {
                AppLogger.Error("创建备份失败。", exception);
                StatusMessage = LocalizationService.T("Loc.Settings.BackupFailed", exception.Message);
            }
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.T("Loc.Settings.BackupFilter"),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            Application.Current.MainWindow,
            LocalizationService.T("Loc.Settings.RestoreConfirm"),
            LocalizationService.T("Loc.Settings.RestoreConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        var prepared = false;
        try
        {
            // 先停止监听并禁止收尾写盘，防止导入后的文件又被内存中的旧数据覆盖。
            await _runtime.PrepareForRestoreAsync();
            prepared = true;
            try
            {
                await _store.ImportBackupAsync(dialog.FileName);
            }
            catch
            {
                await _runtime.ResumeAfterRestoreFailureAsync();
                throw;
            }

            MessageBox.Show(
                Application.Current.MainWindow,
                LocalizationService.T("Loc.Settings.BackupRestoredExit"),
                LocalizationService.T("Loc.Settings.RestoreConfirmTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            App.Current.ExitWithoutSaving();
        }
        catch (Exception exception)
        {
            AppLogger.Error("恢复备份失败。", exception);
            StatusMessage = LocalizationService.T(
                prepared ? "Loc.Settings.RestoreFailedResumed" : "Loc.Settings.RestoreFailed",
                exception.Message);
        }
    }

    [RelayCommand]
    private async Task ValidateDataAsync()
    {
        var errors = await _store.ValidateAsync();
        StatusMessage = errors.Count == 0
            ? LocalizationService.T("Loc.Settings.ValidateOk")
            : LocalizationService.T("Loc.Settings.ValidateIssues", errors.Count);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirmed = MessageBox.Show(
            Application.Current.MainWindow,
            LocalizationService.T("Loc.Settings.ClearHistoryConfirm"),
            LocalizationService.T("Loc.Settings.ClearHistoryConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        await _runtime.ActivityService.SetPausedAsync(true);
        await _runtime.ActivityService.ClearSessionsAsync();
        _runtime.Corrections.Clear();
        await _runtime.SaveSessionsAndCorrectionsAsync();
        _runtime.NotifyDataChanged();
        StatusMessage = LocalizationService.T("Loc.Settings.HistoryCleared");
    }

    /// <summary>加载期间不触发即时生效，避免逐属性回写。</summary>
    private void ApplyIfNotLoading()
    {
        if (_isLoading)
        {
            return;
        }

        if (!TryValidateNumericSettings(out var validationMessage))
        {
            ValidationMessage = validationMessage;
            return;
        }

        ValidationMessage = string.Empty;
        ApplyCurrent();
    }

    /// <summary>把所有当前值写入设置、应用副作用并持久化，实现「修改即时生效」。</summary>
    private void ApplyCurrent()
    {
        var settings = _runtime.Settings;
        var autoStartChanged = settings.AutoStart != AutoStart;
        var hotkeyChanged = !string.Equals(settings.Hotkey, Hotkey, StringComparison.Ordinal);
        var themeChanged = !string.Equals(settings.Theme, Theme, StringComparison.Ordinal);
        var language = LocalizationService.Normalize(Language);
        var languageChanged = !string.Equals(settings.Language, language, StringComparison.Ordinal);

        settings.AutoStart = AutoStart;
        settings.StartMinimized = StartMinimized;
        settings.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        settings.Hotkey = Hotkey;
        settings.ExcludeIdleTime = ExcludeIdleTime;
        settings.IdleThresholdMinutes = Math.Clamp(IdleThresholdMinutes, 1, 120);
        settings.ShortSwitchSeconds = Math.Clamp(ShortSwitchSeconds, 0, 60);
        settings.HeartbeatSeconds = Math.Clamp(HeartbeatSeconds, 5, 300);
        settings.AutoSaveSeconds = Math.Clamp(AutoSaveSeconds, 5, 3600);
        settings.MaxHistoryDays = Math.Clamp(MaxHistoryDays, 7, 3650);
        settings.NotificationsEnabled = NotificationsEnabled;
        settings.DailySummaryHour = Math.Clamp(DailySummaryHour, 0, 23);
        settings.DailySummaryMinute = Math.Clamp(DailySummaryMinute, 0, 59);
        settings.Theme = Theme;
        settings.Language = language;

        if (autoStartChanged)
        {
            try
            {
                _startup.SetEnabled(AutoStart);
            }
            catch (Exception exception)
            {
                AppLogger.Debug($"更新开机启动设置失败：{exception.Message}");
            }
        }

        if (themeChanged)
        {
            ThemeService.Apply(Theme);
        }

        if (languageChanged)
        {
            LocalizationService.Apply(language);
        }

        var hotkeyApplied = true;
        if (hotkeyChanged)
        {
            hotkeyApplied = App.Current.ApplyHotkey();
        }

        _ = PersistAsync();
        StatusMessage = hotkeyApplied
            ? LocalizationService.T("Loc.Settings.AutoSaved")
            : LocalizationService.T("Loc.Settings.HotkeyFailed", Hotkey);
    }

    private bool TryValidateNumericSettings(out string message)
    {
        var invalidKey = IdleThresholdMinutes is < 1 or > 120 ? "Loc.Settings.Range.Idle"
            : ShortSwitchSeconds is < 0 or > 60 ? "Loc.Settings.Range.ShortSwitch"
            : HeartbeatSeconds is < 5 or > 300 ? "Loc.Settings.Range.Heartbeat"
            : AutoSaveSeconds is < 5 or > 3600 ? "Loc.Settings.Range.AutoSave"
            : MaxHistoryDays is < 7 or > 3650 ? "Loc.Settings.Range.History"
            : DailySummaryHour is < 0 or > 23 || DailySummaryMinute is < 0 or > 59
                ? "Loc.Settings.Range.DailySummary"
                : null;
        message = invalidKey is null
            ? string.Empty
            : LocalizationService.T("Loc.Settings.InvalidNumber", LocalizationService.T(invalidKey));
        return invalidKey is null;
    }

    private void OnHotkeyRegistrationChanged(bool succeeded, string hotkey)
    {
        HotkeyStatusMessage = succeeded
            ? string.Empty
            : LocalizationService.T("Loc.Settings.HotkeyFailed", hotkey);
        StatusMessage = succeeded
            ? LocalizationService.T("Loc.Settings.AutoSaved")
            : HotkeyStatusMessage;
    }

    private async Task PersistAsync()
    {
        try
        {
            await _runtime.SaveSettingsAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"设置保存失败：{exception.Message}");
        }
    }

    private void Load()
    {
        _isLoading = true;
        try
        {
            var settings = _runtime.Settings;
            AutoStart = settings.AutoStart;
            StartMinimized = settings.StartMinimized;
            MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
            Hotkey = settings.Hotkey;
            ExcludeIdleTime = settings.ExcludeIdleTime;
            IdleThresholdMinutes = settings.IdleThresholdMinutes;
            ShortSwitchSeconds = settings.ShortSwitchSeconds;
            HeartbeatSeconds = settings.HeartbeatSeconds;
            AutoSaveSeconds = settings.AutoSaveSeconds;
            MaxHistoryDays = settings.MaxHistoryDays;
            NotificationsEnabled = settings.NotificationsEnabled;
            DailySummaryHour = settings.DailySummaryHour;
            DailySummaryMinute = settings.DailySummaryMinute;
            Theme = settings.Theme;
            Language = LocalizationService.Normalize(settings.Language);
        }
        finally
        {
            _isLoading = false;
        }
    }
}
