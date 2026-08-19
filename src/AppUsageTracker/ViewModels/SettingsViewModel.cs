using System.Diagnostics;
using System.IO;
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
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

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
    private bool _saveWindowTitles;

    [ObservableProperty]
    private string _theme = "System";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    partial void OnAutoStartChanged(bool value) => ApplyIfNotLoading();
    partial void OnStartMinimizedChanged(bool value) => ApplyIfNotLoading();
    partial void OnMinimizeToTrayOnCloseChanged(bool value) => ApplyIfNotLoading();
    partial void OnHotkeyChanged(string value) => ApplyIfNotLoading();
    partial void OnExcludeIdleTimeChanged(bool value) => ApplyIfNotLoading();
    partial void OnIdleThresholdMinutesChanged(int value) => ApplyIfNotLoading();
    partial void OnShortSwitchSecondsChanged(int value) => ApplyIfNotLoading();
    partial void OnAutoSaveSecondsChanged(int value) => ApplyIfNotLoading();
    partial void OnMaxHistoryDaysChanged(int value) => ApplyIfNotLoading();
    partial void OnNotificationsEnabledChanged(bool value) => ApplyIfNotLoading();
    partial void OnSaveWindowTitlesChanged(bool value) => ApplyIfNotLoading();
    partial void OnThemeChanged(string value) => ApplyIfNotLoading();

    [RelayCommand]
    private void OpenDataDirectory()
    {
        var path = ((IAppDataPathProvider)new AppDataPathProvider()).RootDirectory;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择备份保存目录",
        };
        if (dialog.ShowDialog() == true)
        {
            var target = await _store.CreateBackupAsync(dialog.FolderName);
            StatusMessage = $"备份已保存：{target}";
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "AppUsageTracker 备份|*.zip",
        };
        if (dialog.ShowDialog() == true)
        {
            await _store.ImportBackupAsync(dialog.FileName);
            StatusMessage = "备份已恢复，请重新启动应用";
        }
    }

    [RelayCommand]
    private async Task ValidateDataAsync()
    {
        var errors = await _store.ValidateAsync();
        StatusMessage = errors.Count == 0
            ? "数据完整性检查通过"
            : $"发现 {errors.Count} 个数据问题";
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await _runtime.ActivityService.SetPausedAsync(true);
        _runtime.Sessions.Clear();
        _runtime.Corrections.Clear();
        await _runtime.SaveSessionsAndCorrectionsAsync();
        _runtime.NotifyDataChanged();
        StatusMessage = "历史会话和修正记录已清除";
    }

    /// <summary>加载期间不触发即时生效，避免逐属性回写。</summary>
    private void ApplyIfNotLoading()
    {
        if (_isLoading)
        {
            return;
        }

        ApplyCurrent();
    }

    /// <summary>把所有当前值写入设置、应用副作用并持久化，实现「修改即时生效」。</summary>
    private void ApplyCurrent()
    {
        var settings = _runtime.Settings;
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
        settings.SaveWindowTitles = SaveWindowTitles;
        settings.Theme = Theme;

        _startup.SetEnabled(AutoStart);
        ThemeService.Apply(Theme);
        App.Current.ApplyHotkey();
        _ = PersistAsync();
        StatusMessage = "设置已自动保存";
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
            SaveWindowTitles = settings.SaveWindowTitles;
            Theme = settings.Theme;
        }
        finally
        {
            _isLoading = false;
        }
    }
}
