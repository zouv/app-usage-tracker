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
        SettingsViewModel settings)
    {
        _runtime = runtime;
        Overview = overview;
        Apps = apps;
        Statistics = statistics;
        Timeline = timeline;
        Settings = settings;
        SelectedPage = Overview;
        _runtime.SnapshotChanged += (_, snapshot) =>
            App.Current.Dispatcher.Invoke(() =>
            {
                ListenerStatus = snapshot.State switch
                {
                    Models.ActivityState.Paused => "监听已暂停",
                    Models.ActivityState.Private => "隐私模式",
                    Models.ActivityState.Stopped => "监听未启动",
                    _ => "监听运行中",
                };
                IsPrivateMode = snapshot.IsPrivateMode;
            });
    }

    public OverviewViewModel Overview { get; }

    public AppsViewModel Apps { get; }

    public StatisticsViewModel Statistics { get; }

    public TimelineViewModel Timeline { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private object _selectedPage;

    [ObservableProperty]
    private string _selectedPageKey = "Overview";

    [ObservableProperty]
    private string _listenerStatus = "监听运行中";

    [ObservableProperty]
    private bool _isPrivateMode;

    [RelayCommand]
    private void Navigate(string page)
    {
        SelectedPageKey = page;
        SelectedPage = page switch
        {
            "Apps" => Apps,
            "Statistics" => Statistics,
            "Timeline" => Timeline,
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
