using System.ComponentModel;
using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using AppUsageTracker.Models;
using AppUsageTracker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppUsageTracker.ViewModels;

public partial class AppsViewModel : ObservableObject
{
    /// <summary>「全部分类」的稳定哨兵值，与持久化的分类名不会冲突。</summary>
    private const string AllCategoryValue = "";

    private const string AllStatusValue = "all";
    private const string EnabledStatusValue = "enabled";
    private const string DisabledStatusValue = "disabled";

    private readonly AppRuntime _runtime;
    private Guid? _editingId;

    public AppsViewModel(AppRuntime runtime)
    {
        _runtime = runtime;
        Apps = new ObservableCollection<TrackedApp>(_runtime.Apps);
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = FilterApp;
        Categories = BuildCategoryOptions();
        StatusOptions = BuildStatusOptions();
        TrackingModes = BuildTrackingModeOptions();
        RunningProcesses = new ObservableCollection<RunningProcessInfo>();
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<TrackedApp> Apps { get; }

    public ICollectionView AppsView { get; }

    [ObservableProperty]
    private IReadOnlyList<OptionItem> _categories = [];

    [ObservableProperty]
    private IReadOnlyList<OptionItem> _statusOptions = [];

    [ObservableProperty]
    private IReadOnlyList<OptionItem> _trackingModes = [];

    public ObservableCollection<RunningProcessInfo> RunningProcesses { get; }

    /// <summary>语言切换后重建下拉标签，并重置列表让分类/模式列按新语言重绘。</summary>
    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        Categories = BuildCategoryOptions();
        StatusOptions = BuildStatusOptions();
        TrackingModes = BuildTrackingModeOptions();

        // 重置列表是让 DataGrid 的列转换器重绘的 UI 操作，必须在调度程序线程上执行；
        // 无 Application 的环境（单元测试）没有界面，直接跳过，避免跨线程改动 CollectionView。
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ResetApps();
        }
        else
        {
            dispatcher.Invoke(ResetApps);
        }
    }

    private void ResetApps()
    {
        var apps = Apps.ToList();
        Apps.Clear();
        foreach (var app in apps)
        {
            Apps.Add(app);
        }

        AppsView.Refresh();
    }

    private static IReadOnlyList<OptionItem> BuildCategoryOptions() =>
    [
        new(AllCategoryValue, LocalizationService.T("Loc.Apps.Category.All")),
        new("未分类", LocalizationService.T("Loc.Apps.Category.Uncategorized")),
        new("开发工具", LocalizationService.T("Loc.Apps.Category.Development")),
        new("浏览器", LocalizationService.T("Loc.Apps.Category.Browser")),
        new("游戏", LocalizationService.T("Loc.Apps.Category.Game")),
        new("通讯", LocalizationService.T("Loc.Apps.Category.Communication")),
        new("影音", LocalizationService.T("Loc.Apps.Category.Media")),
        new("办公", LocalizationService.T("Loc.Apps.Category.Office")),
    ];

    private static IReadOnlyList<OptionItem> BuildStatusOptions() =>
    [
        new(AllStatusValue, LocalizationService.T("Loc.Apps.Status.All")),
        new(EnabledStatusValue, LocalizationService.T("Loc.Apps.Status.Enabled")),
        new(DisabledStatusValue, LocalizationService.T("Loc.Apps.Status.Disabled")),
    ];

    private static IReadOnlyList<OptionItem> BuildTrackingModeOptions() =>
    [
        new(TrackingMode.Effective, LocalizationService.T("Loc.Apps.Mode.Effective")),
        new(TrackingMode.Foreground, LocalizationService.T("Loc.Apps.Mode.Foreground")),
        new(TrackingMode.Running, LocalizationService.T("Loc.Apps.Mode.Running")),
    ];

    [ObservableProperty]
    private TrackedApp? _selectedApp;

    [ObservableProperty]
    private RunningProcessInfo? _selectedProcess;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => AppsView.Refresh();

    [ObservableProperty]
    private string _selectedCategory = AllCategoryValue;

    partial void OnSelectedCategoryChanged(string value) => AppsView.Refresh();

    [ObservableProperty]
    private string _selectedStatus = AllStatusValue;

    partial void OnSelectedStatusChanged(string value) => AppsView.Refresh();

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private string _editorTitle = LocalizationService.T("Loc.Apps.EditorAdd");

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editProcessName = string.Empty;

    [ObservableProperty]
    private string _editExecutablePath = string.Empty;

    [ObservableProperty]
    private string _editCategory = "未分类";

    [ObservableProperty]
    private string _editColorHex = "#2F6BDE";

    [ObservableProperty]
    private string _editIconBase64 = string.Empty;

    [ObservableProperty]
    private TrackingMode _editTrackingMode = TrackingMode.Effective;

    [ObservableProperty]
    private bool _editEnabled = true;

    [ObservableProperty]
    private bool _editIgnoreIdle;

    [ObservableProperty]
    private string _editWindowTitle = string.Empty;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [RelayCommand]
    private void AddNew()
    {
        _editingId = null;
        EditorTitle = LocalizationService.T("Loc.Apps.EditorAdd");
        EditName = string.Empty;
        EditProcessName = string.Empty;
        EditExecutablePath = string.Empty;
        EditCategory = "未分类";
        EditColorHex = "#2F6BDE";
        EditIconBase64 = string.Empty;
        EditTrackingMode = TrackingMode.Effective;
        EditEnabled = true;
        EditIgnoreIdle = false;
        EditWindowTitle = string.Empty;
        EditNotes = string.Empty;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedApp is null)
        {
            return;
        }

        _editingId = SelectedApp.Id;
        EditorTitle = LocalizationService.T("Loc.Apps.EditorEdit", SelectedApp.Name);
        EditName = SelectedApp.Name;
        EditProcessName = SelectedApp.ProcessName;
        EditExecutablePath = SelectedApp.ExecutablePath;
        EditCategory = SelectedApp.Category;
        EditColorHex = SelectedApp.ColorHex;
        EditIconBase64 = SelectedApp.IconBase64;
        EditTrackingMode = SelectedApp.TrackingMode;
        EditEnabled = SelectedApp.Enabled;
        EditIgnoreIdle = SelectedApp.IgnoreIdle;
        EditWindowTitle = SelectedApp.MatchRules
            .FirstOrDefault(rule => rule.Type == MatchRuleType.WindowTitleContains)?.Pattern ?? string.Empty;
        EditNotes = SelectedApp.Notes;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName) ||
            (string.IsNullOrWhiteSpace(EditProcessName) &&
             string.IsNullOrWhiteSpace(EditExecutablePath)))
        {
            return;
        }

        var app = _editingId is { } id
            ? Apps.FirstOrDefault(item => item.Id == id)
            : null;
        if (app is null)
        {
            app = new TrackedApp();
            Apps.Add(app);
            _runtime.Apps.Add(app);
        }

        app.Name = EditName.Trim();
        app.ProcessName = EditProcessName.Trim();
        app.ExecutablePath = EditExecutablePath.Trim();
        app.Category = EditCategory;
        app.ColorHex = NormalizeColor(EditColorHex);
        app.IconBase64 = EditIconBase64;
        app.TrackingMode = EditTrackingMode;
        app.Enabled = EditEnabled;
        app.IgnoreIdle = EditIgnoreIdle;
        app.Notes = EditNotes.Trim();
        app.MatchRules.RemoveAll(rule => rule.Type == MatchRuleType.WindowTitleContains);
        if (!string.IsNullOrWhiteSpace(EditWindowTitle))
        {
            app.MatchRules.Add(new MatchRule
            {
                Type = MatchRuleType.WindowTitleContains,
                Pattern = EditWindowTitle.Trim(),
                Priority = 20,
            });
        }

        await _runtime.SaveAppsAsync();
        AppsView.Refresh();
        IsEditorOpen = false;
    }

    [RelayCommand]
    private void CancelEditor() => IsEditorOpen = false;

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedApp is null)
        {
            return;
        }

        _runtime.Apps.RemoveAll(item => item.Id == SelectedApp.Id);
        Apps.Remove(SelectedApp);
        await _runtime.SaveAppsAsync();
    }

    [RelayCommand]
    private async Task ToggleSelectedAsync()
    {
        if (SelectedApp is null)
        {
            return;
        }

        SelectedApp.Enabled = !SelectedApp.Enabled;
        await _runtime.SaveAppsAsync();
        AppsView.Refresh();
    }

    [RelayCommand]
    private async Task EnableFilteredAsync()
    {
        foreach (var app in AppsView.Cast<TrackedApp>())
        {
            app.Enabled = true;
        }

        await _runtime.SaveAppsAsync();
        AppsView.Refresh();
    }

    [RelayCommand]
    private async Task DisableFilteredAsync()
    {
        foreach (var app in AppsView.Cast<TrackedApp>())
        {
            app.Enabled = false;
        }

        await _runtime.SaveAppsAsync();
        AppsView.Refresh();
    }

    [RelayCommand]
    private void ScanProcesses()
    {
        RunningProcesses.Clear();
        foreach (var process in _runtime.ProcessScanner.EnumerateVisibleProcesses())
        {
            RunningProcesses.Add(process);
        }
    }

    [RelayCommand]
    private void UseSelectedProcess()
    {
        if (SelectedProcess is null)
        {
            return;
        }

        AddNew();
        EditProcessName = SelectedProcess.ProcessName;
        EditExecutablePath = SelectedProcess.ExecutablePath;
        EditName = !string.IsNullOrWhiteSpace(SelectedProcess.WindowTitle)
            ? SelectedProcess.WindowTitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries).Last()
            : Path.GetFileNameWithoutExtension(SelectedProcess.ProcessName);
        EditIconBase64 = SelectedProcess.IconBase64;
        RunningProcesses.Clear();
    }

    private bool FilterApp(object item)
    {
        if (item is not TrackedApp app)
        {
            return false;
        }

        var searchMatch = string.IsNullOrWhiteSpace(SearchText) ||
                          app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                          app.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                          app.ExecutablePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        var categoryMatch = SelectedCategory == AllCategoryValue || app.Category == SelectedCategory;
        var statusMatch = SelectedStatus switch
        {
            EnabledStatusValue => app.Enabled,
            DisabledStatusValue => !app.Enabled,
            _ => true,
        };
        return searchMatch && categoryMatch && statusMatch;
    }

    private static string NormalizeColor(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$")
            ? value
            : "#2F6BDE";
}
