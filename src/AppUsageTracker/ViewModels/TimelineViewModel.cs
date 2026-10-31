using System.Collections.ObjectModel;
using System.Windows;
using AppUsageTracker.Models;
using AppUsageTracker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AppUsageTracker.ViewModels;

public partial class TimelineViewModel : ObservableObject
{
    private readonly AppRuntime _runtime;
    private readonly ISessionEditor _editor;
    private readonly CsvExportService _exporter;
    private readonly IConfirmationService _confirmationService;
    private int _refreshPending;
    private bool _isActive;

    public TimelineViewModel(
        AppRuntime runtime,
        ISessionEditor editor,
        CsvExportService exporter,
        IConfirmationService? confirmationService = null)
    {
        _runtime = runtime;
        _editor = editor;
        _exporter = exporter;
        _confirmationService = confirmationService ?? new MessageBoxConfirmationService();
        SelectedDate = DateTime.Today;
        Apps = _runtime.Apps.ToList();
        _runtime.DataChanged += (_, _) => RequestRefresh();
        LocalizationService.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    public ObservableCollection<SessionRow> Sessions { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<OptionItem> _appFilterOptions = [];

    [ObservableProperty]
    private string _selectedAppFilter = string.Empty;

    partial void OnSelectedAppFilterChanged(string value) => Refresh();

    [ObservableProperty]
    private IReadOnlyList<OptionItem> _sourceFilterOptions = BuildSourceFilterOptions();

    [ObservableProperty]
    private string _selectedSourceFilter = "all";

    partial void OnSelectedSourceFilterChanged(string value) => Refresh();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSessionCommand))]
    private IReadOnlyList<TrackedApp> _apps = [];

    public bool IsActive => _isActive;

    public void Activate()
    {
        _isActive = true;
        Apps = _runtime.Apps.ToList();
        RebuildAppFilterOptions();
        Refresh();
    }

    private void RebuildAppFilterOptions()
    {
        var selectedFilter = SelectedAppFilter;
        AppFilterOptions =
        [
            new OptionItem(string.Empty, LocalizationService.T("Loc.Timeline.Filter.AllApps")),
            .. Apps.Select(app => new OptionItem(app.Id.ToString(), app.Name)),
        ];
        SelectedAppFilter = AppFilterOptions.Any(option => (string)option.Value == selectedFilter)
            ? selectedFilter
            : string.Empty;
    }

    private void OnLanguageChanged()
    {
        SourceFilterOptions = BuildSourceFilterOptions();
        RebuildAppFilterOptions();
        RequestRefresh();
    }

    private static IReadOnlyList<OptionItem> BuildSourceFilterOptions() =>
    [
        new("all", LocalizationService.T("Loc.Timeline.Filter.AllSources")),
        new("automatic", LocalizationService.T("Loc.Timeline.Filter.Automatic")),
        new("manual", LocalizationService.T("Loc.Timeline.Filter.Manual")),
        new("active", LocalizationService.T("Loc.Timeline.Ongoing")),
    ];

    public void Deactivate() => _isActive = false;

    private void RequestRefresh()
    {
        if (!_isActive || !IsWindowVisible())
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
        {
            return;
        }

        dispatcher.BeginInvoke(
            () =>
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                if (_isActive && IsWindowVisible())
                {
                    Refresh();
                }
            });
    }

    private static bool IsWindowVisible() =>
        Application.Current?.MainWindow is
            { IsVisible: true, WindowState: not WindowState.Minimized };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextDayCommand))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextDay))]
    private DateTime _selectedDate;

    public bool CanGoToNextDay => SelectedDate.Date < DateTime.Today;

    partial void OnSelectedDateChanged(DateTime value) => Refresh();

    partial void OnSelectedSessionChanged(SessionRow? value)
    {
        StatusMessage = string.Empty;
        EditSessionCommand.NotifyCanExecuteChanged();
        DeleteSessionCommand.NotifyCanExecuteChanged();
        MarkAsIdleCommand.NotifyCanExecuteChanged();
        MergeWithPreviousCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private SessionRow? _selectedSession;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditorCommand))]
    private TrackedApp? _editApp;

    partial void OnEditAppChanged(TrackedApp? value) => UpdateEditorError();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditorCommand))]
    private DateTime _editStart = DateTime.Now;

    partial void OnEditStartChanged(DateTime value) => UpdateEditorError();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditorCommand))]
    private DateTime _editEnd = DateTime.Now;

    partial void OnEditEndChanged(DateTime value) => UpdateEditorError();

    [ObservableProperty]
    private string _editorError = string.Empty;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [RelayCommand]
    private void PreviousDay() => SelectedDate = SelectedDate.AddDays(-1);

    [RelayCommand(CanExecute = nameof(CanGoToNextDay))]
    private void NextDay() => SelectedDate = SelectedDate.AddDays(1);

    [RelayCommand(CanExecute = nameof(HasApps))]
    private void AddSession()
    {
        if (!HasApps())
        {
            return;
        }

        EditorError = string.Empty;
        EditApp = Apps.FirstOrDefault();
        EditStart = SelectedDate.Date.AddHours(9);
        EditEnd = EditStart.AddMinutes(30);
        EditNotes = string.Empty;
        SelectedSession = null;
        IsEditorOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedSession))]
    private void EditSession()
    {
        if (SelectedSession is null)
        {
            return;
        }

        if (!EnsureCompleted(SelectedSession))
        {
            return;
        }

        EditorError = string.Empty;
        EditApp = Apps.FirstOrDefault(app => app.Id == SelectedSession.Session.ApplicationId);
        EditStart = SelectedSession.Session.StartedAtUtc.ToLocalTime();
        EditEnd = SelectedSession.Session.EndedAtUtc?.ToLocalTime() ?? EditStart;
        EditNotes = SelectedSession.Session.Notes;
        IsEditorOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanSaveEditor))]
    private async Task SaveEditorAsync()
    {
        var editApp = EditApp;
        if (editApp is null || EditEnd <= EditStart)
        {
            UpdateEditorError();
            return;
        }

        EditorError = string.Empty;

        if (SelectedSession is null)
        {
            if (!await TryEditAsync(() => _editor.AddAsync(new ActivitySession
                {
                    ApplicationId = editApp.Id,
                    StartedAtUtc = EditStart.ToUniversalTime(),
                    EndedAtUtc = EditEnd.ToUniversalTime(),
                    State = ActivityState.Active,
                    EndReason = SessionEndReason.Manual,
                    Notes = EditNotes,
                })))
            {
                return;
            }
        }
        else
        {
            var session = SelectedSession.Session.Clone();
            session.ApplicationId = editApp.Id;
            session.StartedAtUtc = EditStart.ToUniversalTime();
            session.EndedAtUtc = EditEnd.ToUniversalTime();
            session.EndReason = SessionEndReason.Manual;
            session.Notes = EditNotes;
            if (!await TryEditAsync(() => _editor.UpdateAsync(session)))
            {
                return;
            }
        }

        IsEditorOpen = false;
        Refresh();
    }

    [RelayCommand]
    private void CancelEditor() => IsEditorOpen = false;

    [RelayCommand(CanExecute = nameof(CanModifySelectedSession))]
    private async Task DeleteSessionAsync()
    {
        var selected = SelectedSession;
        if (selected is null || !EnsureCompleted(selected))
        {
            return;
        }

        if (!_confirmationService.Confirm(
                LocalizationService.T(
                    "Loc.Timeline.DeleteConfirm",
                    selected.ApplicationName,
                    selected.TimeRange),
                LocalizationService.T("Loc.Timeline.DeleteConfirmTitle")))
        {
            return;
        }

        if (await TryEditAsync(() => _editor.DeleteAsync(selected.Session.Id)))
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMergeWithPrevious))]
    private async Task MergeWithPreviousAsync()
    {
        if (!CanMergeWithPrevious())
        {
            return;
        }

        var selected = SelectedSession!;
        var index = Sessions.IndexOf(selected);
        if (!_confirmationService.Confirm(
                LocalizationService.T("Loc.Timeline.MergeConfirm", selected.ApplicationName),
                LocalizationService.T("Loc.Timeline.MergeConfirmTitle")))
        {
            return;
        }

        if (await TryEditAsync(
                () => _editor.MergeAsync(Sessions[index - 1].Session.Id, selected.Session.Id)))
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedSession))]
    private async Task MarkAsIdleAsync()
    {
        if (SelectedSession is null || !EnsureCompleted(SelectedSession))
        {
            return;
        }

        var session = SelectedSession.Session.Clone();
        session.ApplicationId = null;
        session.State = ActivityState.Idle;
        session.EndReason = SessionEndReason.Manual;
        if (await TryEditAsync(() => _editor.UpdateAsync(session)))
        {
            Refresh();
        }
    }

    [RelayCommand]
    private async Task ClearSelectedDayAsync()
    {
        if (!_confirmationService.Confirm(
                LocalizationService.T("Loc.Timeline.ClearDayConfirm", SelectedDate.ToString("yyyy-MM-dd")),
                LocalizationService.T("Loc.Timeline.ClearDayConfirmTitle")))
        {
            return;
        }

        var ids = _runtime.Sessions
            .Where(item => item.StartedAtUtc.ToLocalTime().Date == SelectedDate.Date &&
                           item.EndedAtUtc is not null)
            .Select(item => item.Id)
            .ToList();
        if (ids.Count > 0 && !await TryEditAsync(() => _editor.DeleteRangeAsync(ids)))
        {
            return;
        }

        Refresh();
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = LocalizationService.T("Loc.Timeline.CsvFilter"),
            FileName = $"usage-sessions-{SelectedDate:yyyyMMdd}.csv",
        };
        if (dialog.ShowDialog() == true)
        {
            await _exporter.ExportSessionsAsync(
                dialog.FileName,
                GetFilteredSessions(),
                _runtime.Apps);
        }
    }

    private async Task<bool> TryEditAsync(Func<Task> action)
    {
        try
        {
            await action();
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            AppLogger.Error("编辑会话失败。", exception);
            StatusMessage = exception.Message;
            return false;
        }
    }

    private bool HasApps() => Apps.Count > 0;

    private bool CanSaveEditor() => EditApp is not null && EditEnd > EditStart;

    private void UpdateEditorError()
    {
        EditorError = EditApp is null
            ? LocalizationService.T("Loc.Timeline.Editor.AppRequired")
            : EditEnd <= EditStart
                ? LocalizationService.T("Loc.Timeline.Editor.EndAfterStart")
                : string.Empty;
    }

    private bool CanModifySelectedSession() => SelectedSession?.Session.EndedAtUtc is not null;

    private bool CanMergeWithPrevious()
    {
        var selected = SelectedSession;
        if (selected?.Session.EndedAtUtc is null)
        {
            return false;
        }

        var index = Sessions.IndexOf(selected);
        return index > 0 &&
               Sessions[index - 1].Session.EndedAtUtc is not null &&
               Sessions[index - 1].Session.ApplicationId == selected.Session.ApplicationId;
    }

    private IReadOnlyList<ActivitySession> GetFilteredSessions() =>
        _runtime.Sessions
            .Where(item => item.StartedAtUtc.ToLocalTime().Date == SelectedDate.Date)
            .Where(item => string.IsNullOrEmpty(SelectedAppFilter) ||
                           item.ApplicationId?.ToString() == SelectedAppFilter)
            .Where(item => SelectedSourceFilter switch
            {
                "manual" => item.IsManual && item.EndedAtUtc is not null,
                "automatic" => !item.IsManual && item.EndedAtUtc is not null,
                "active" => item.EndedAtUtc is null,
                _ => true,
            })
            .OrderBy(item => item.StartedAtUtc)
            .ToList();

    private bool EnsureCompleted(SessionRow row)
    {
        if (row.Session.EndedAtUtc is not null)
        {
            return true;
        }

        StatusMessage = LocalizationService.T("Loc.Timeline.ActiveSessionLocked");
        return false;
    }

    public void Refresh()
    {
        if (!_isActive || !IsWindowVisible())
        {
            return;
        }

        var appMap = _runtime.Apps.ToDictionary(app => app.Id);
        var rows = GetFilteredSessions()
            .Select(item => new SessionRow
            {
                Session = item,
                ApplicationName = item.ApplicationId is { } appId
                    ? appMap.GetValueOrDefault(appId)?.Name ?? LocalizationService.T("Loc.Timeline.DeletedApp")
                    : LocalizationService.ActivityStateLabel(item.State),
            })
            .ToList();
        var selectedId = SelectedSession?.Session.Id;
        Sessions.Clear();
        foreach (var row in rows)
        {
            Sessions.Add(row);
        }

        SelectedSession = rows.FirstOrDefault(row => row.Session.Id == selectedId);
        MergeWithPreviousCommand.NotifyCanExecuteChanged();
    }
}
