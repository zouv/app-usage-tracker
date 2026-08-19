using System.Collections.ObjectModel;
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

    public TimelineViewModel(
        AppRuntime runtime,
        ISessionEditor editor,
        CsvExportService exporter)
    {
        _runtime = runtime;
        _editor = editor;
        _exporter = exporter;
        SelectedDate = DateTime.Today;
        _runtime.DataChanged += (_, _) => App.Current.Dispatcher.Invoke(Refresh);
        Refresh();
    }

    public ObservableCollection<SessionRow> Sessions { get; } = [];

    public IReadOnlyList<TrackedApp> Apps => _runtime.Apps;

    [ObservableProperty]
    private DateTime _selectedDate;

    partial void OnSelectedDateChanged(DateTime value) => Refresh();

    [ObservableProperty]
    private SessionRow? _selectedSession;

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private TrackedApp? _editApp;

    [ObservableProperty]
    private DateTime _editStart = DateTime.Now;

    [ObservableProperty]
    private DateTime _editEnd = DateTime.Now;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [RelayCommand]
    private void PreviousDay() => SelectedDate = SelectedDate.AddDays(-1);

    [RelayCommand]
    private void NextDay() => SelectedDate = SelectedDate.AddDays(1);

    [RelayCommand]
    private void AddSession()
    {
        EditApp = Apps.FirstOrDefault();
        EditStart = SelectedDate.Date.AddHours(9);
        EditEnd = EditStart.AddMinutes(30);
        EditNotes = string.Empty;
        SelectedSession = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void EditSession()
    {
        if (SelectedSession is null)
        {
            return;
        }

        EditApp = Apps.FirstOrDefault(app => app.Id == SelectedSession.Session.ApplicationId);
        EditStart = SelectedSession.Session.StartedAtUtc.ToLocalTime();
        EditEnd = SelectedSession.Session.EndedAtUtc?.ToLocalTime() ?? EditStart;
        EditNotes = SelectedSession.Session.Notes;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (EditApp is null || EditEnd <= EditStart)
        {
            return;
        }

        if (SelectedSession is null)
        {
            await _editor.AddAsync(new ActivitySession
            {
                ApplicationId = EditApp.Id,
                StartedAtUtc = EditStart.ToUniversalTime(),
                EndedAtUtc = EditEnd.ToUniversalTime(),
                State = ActivityState.Active,
                EndReason = SessionEndReason.Manual,
                Notes = EditNotes,
            });
        }
        else
        {
            var session = SelectedSession.Session.Clone();
            session.ApplicationId = EditApp.Id;
            session.StartedAtUtc = EditStart.ToUniversalTime();
            session.EndedAtUtc = EditEnd.ToUniversalTime();
            session.EndReason = SessionEndReason.Manual;
            session.Notes = EditNotes;
            await _editor.UpdateAsync(session);
        }

        IsEditorOpen = false;
        Refresh();
    }

    [RelayCommand]
    private void CancelEditor() => IsEditorOpen = false;

    [RelayCommand]
    private async Task DeleteSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        await _editor.DeleteAsync(SelectedSession.Session.Id);
        Refresh();
    }

    [RelayCommand]
    private async Task MergeWithPreviousAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var index = Sessions.IndexOf(SelectedSession);
        if (index <= 0)
        {
            return;
        }

        await _editor.MergeAsync(Sessions[index - 1].Session.Id, SelectedSession.Session.Id);
        Refresh();
    }

    [RelayCommand]
    private async Task MarkAsIdleAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var session = SelectedSession.Session.Clone();
        session.ApplicationId = null;
        session.State = ActivityState.Idle;
        session.EndReason = SessionEndReason.Manual;
        await _editor.UpdateAsync(session);
        Refresh();
    }

    [RelayCommand]
    private async Task ClearSelectedDayAsync()
    {
        var ids = _runtime.Sessions
            .Where(item => item.StartedAtUtc.ToLocalTime().Date == SelectedDate.Date)
            .Select(item => item.Id)
            .ToList();
        foreach (var id in ids)
        {
            await _editor.DeleteAsync(id);
        }

        Refresh();
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"usage-sessions-{SelectedDate:yyyyMMdd}.csv",
        };
        if (dialog.ShowDialog() == true)
        {
            await _exporter.ExportSessionsAsync(dialog.FileName, _runtime.Sessions, _runtime.Apps);
        }
    }

    public void Refresh()
    {
        var appMap = _runtime.Apps.ToDictionary(app => app.Id);
        var rows = _runtime.Sessions
            .Where(item => item.StartedAtUtc.ToLocalTime().Date == SelectedDate.Date)
            .OrderBy(item => item.StartedAtUtc)
            .Select(item => new SessionRow
            {
                Session = item,
                ApplicationName = item.ApplicationId is { } appId
                    ? appMap.GetValueOrDefault(appId)?.Name ?? "已删除软件"
                    : item.State.ToString(),
            })
            .ToList();
        Sessions.Clear();
        foreach (var row in rows)
        {
            Sessions.Add(row);
        }
    }
}
