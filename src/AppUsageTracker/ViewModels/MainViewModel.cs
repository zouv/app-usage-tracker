using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUsageTracker.Services;

namespace AppUsageTracker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppRuntime _runtime;

    public MainViewModel(
        AppRuntime runtime,
        OverviewViewModel overview,
        AppsViewModel apps,
        StatisticsViewModel statistics,
        TimelineViewModel timeline,
        TimerViewModel timer,
        SettingsViewModel settings)
    {
        _runtime = runtime;
        Overview = overview;
        Apps = apps;
        Statistics = statistics;
        Timeline = timeline;
        Timer = timer;
        Settings = settings;
        SelectedPage = Overview;
        _runtime.SnapshotChanged += (_, snapshot) =>
        {
            var dispatcher = App.Current.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                ApplyListenerState(snapshot);
                return;
            }

            dispatcher.BeginInvoke(() => ApplyListenerState(snapshot));
        };
        LocalizationService.LanguageChanged += (_, _) => ListenerStatus = StatusText(_runtime.Snapshot.State);
        ApplyListenerState(_runtime.Snapshot);
    }

    private void ApplyListenerState(TrackingSnapshot snapshot)
    {
        ListenerStatus = StatusText(snapshot.State);
        ListenerStatusKind = snapshot.State switch
        {
            Models.ActivityState.Paused or Models.ActivityState.Private => "warning",
            Models.ActivityState.Idle or Models.ActivityState.Locked
                or Models.ActivityState.Suspended => "warning",
            Models.ActivityState.Stopped => "muted",
            _ => "success",
        };
        IsPrivateMode = snapshot.IsPrivateMode;
    }

    private static string StatusText(Models.ActivityState state) =>
        LocalizationService.T(state switch
        {
            Models.ActivityState.Paused => "Loc.Status.Paused",
            Models.ActivityState.Private => "Loc.Status.Private",
            Models.ActivityState.Stopped => "Loc.Status.Stopped",
            Models.ActivityState.Idle => "Loc.State.Idle",
            Models.ActivityState.Locked => "Loc.State.Locked",
            Models.ActivityState.Suspended => "Loc.State.Suspended",
            _ => "Loc.Status.Running",
        });

    public OverviewViewModel Overview { get; }

    /// <summary>侧边栏底部显示的版本号，取自程序集版本。</summary>
    public string AppVersion { get; } = FormatAppVersion();

    private static string FormatAppVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "v1.0.0" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public AppsViewModel Apps { get; }

    public StatisticsViewModel Statistics { get; }

    public TimelineViewModel Timeline { get; }

    /// <summary>定时器独立页面的状态与命令。</summary>
    public TimerViewModel Timer { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private object _selectedPage;

    [ObservableProperty]
    private string _selectedPageKey = "Overview";

    [ObservableProperty]
    private string _listenerStatus = LocalizationService.T("Loc.Status.Running");

    [ObservableProperty]
    private bool _isPrivateMode;

    [ObservableProperty]
    private string _listenerStatusKind = "success";

    [RelayCommand]
    private void Navigate(string page)
    {
        SelectedPageKey = page;
        SelectedPage = page switch
        {
            "Apps" => Apps,
            "Statistics" => Statistics,
            "Timeline" => Timeline,
            "Timer" => Timer,
            "Settings" => Settings,
            _ => Overview,
        };
    }

    [RelayCommand]
    private async Task TogglePrivateModeAsync()
    {
        await _runtime.ActivityService.SetPrivateModeAsync(!IsPrivateMode);
    }
}
