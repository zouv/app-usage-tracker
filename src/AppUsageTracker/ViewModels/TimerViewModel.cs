using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AppUsageTracker.Models;
using AppUsageTracker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppUsageTracker.ViewModels;

/// <summary>
/// 定时器独立页面与桌面倒计时面板的状态与命令。
/// 设置修改即时生效并持久化；面板窗口由本 ViewModel 持有，随倒计时运行状态显隐。
/// </summary>
public partial class TimerViewModel : ObservableObject
{
    private const int MaxDurationSeconds = 99 * 3600 + 59 * 60 + 59;

    private readonly AppRuntime _runtime;
    private readonly CountdownTimerService _service;
    private readonly Dispatcher _dispatcher;
    private bool _isLoading;
    private Views.CountdownPanelWindow? _panel;
    private double _panelFontSize = 32;
    private double _panelStatusFontSize = 13;
    private Thickness _panelPadding = new(14, 10, 14, 10);

    /// <summary>面板数字字体：与 CountdownPanelWindow 数字 TextBlock 的 FontFamily/FontWeight 保持一致，供精确测宽。</summary>
    private static readonly Typeface PanelDigitsTypeface = new(
        SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public TimerViewModel(AppRuntime runtime, CountdownTimerService service)
    {
        _runtime = runtime;
        _service = service;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _service.Changed += OnServiceChanged;
        LocalizationService.LanguageChanged += (_, _) => EndActionOptions = BuildEndActionOptions();
        Load();
        RefreshState();
    }

    [ObservableProperty]
    private IReadOnlyList<OptionItem> _endActionOptions = BuildEndActionOptions();

    [ObservableProperty]
    private int _durationHours;

    [ObservableProperty]
    private int _durationMinutes = 25;

    [ObservableProperty]
    private int _durationSeconds;

    [ObservableProperty]
    private bool _showPanel = true;

    [ObservableProperty]
    private int _panelWidth = 220;

    [ObservableProperty]
    private int _panelTextOpacity = 100;

    [ObservableProperty]
    private int _panelBaseOpacity = 90;

    [ObservableProperty]
    private int _overlayOpacity = 50;

    [ObservableProperty]
    private TimerEndAction _endAction = TimerEndAction.LockScreen;

    [ObservableProperty]
    private string _remainingText = "00:00";

    [ObservableProperty]
    private bool _isTimerActive;

    [ObservableProperty]
    private bool _isTimerPaused;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    /// <summary>时/分/秒合成的总秒数。</summary>
    public int TotalSeconds => DurationHours * 3600 + DurationMinutes * 60 + DurationSeconds;

    /// <summary>结束动作为全屏桌布时才显示桌布透明度设置行。</summary>
    public bool IsOverlayAction => EndAction == TimerEndAction.ScreenOverlay;

    /// <summary>面板文字不透明度（0.2–1.0），供面板文字层 Opacity 绑定。</summary>
    public double PanelTextOpacityFraction => Math.Clamp(PanelTextOpacity, 20, 100) / 100.0;

    /// <summary>面板底板不透明度（0–1.0），供面板底板层 Opacity 绑定。</summary>
    public double PanelBaseOpacityFraction => Math.Clamp(PanelBaseOpacity, 0, 100) / 100.0;

    /// <summary>桌布底板不透明度（0.1–1.0），供桌布背景层 Opacity 绑定。</summary>
    public double OverlayOpacityFraction => Math.Clamp(OverlayOpacity, 10, 100) / 100.0;

    /// <summary>剩余时间字号：随面板宽度与时间格式自适应，窄面板不再排版变形。</summary>
    public double PanelFontSize => _panelFontSize;

    /// <summary>面板状态文字字号：随剩余时间字号缩放，过窄时靠换行容纳。</summary>
    public double PanelStatusFontSize => _panelStatusFontSize;

    /// <summary>面板内边距：窄面板收紧左右留白，给数字让出可用宽度。</summary>
    public Thickness PanelPadding => _panelPadding;

    /// <summary>上次拖拽保存的面板位置；null 时窗口使用默认位置。</summary>
    public double? SavedPanelLeft => _runtime.Settings.TimerPanelLeft;

    /// <summary>上次拖拽保存的面板位置；null 时窗口使用默认位置。</summary>
    public double? SavedPanelTop => _runtime.Settings.TimerPanelTop;

    partial void OnDurationHoursChanged(int value) => ApplyIfNotLoading();
    partial void OnDurationMinutesChanged(int value) => ApplyIfNotLoading();
    partial void OnDurationSecondsChanged(int value) => ApplyIfNotLoading();
    partial void OnShowPanelChanged(bool value) => ApplyIfNotLoading();
    partial void OnPanelWidthChanged(int value)
    {
        UpdateFontSizes();
        ApplyIfNotLoading();
    }
    partial void OnPanelTextOpacityChanged(int value)
    {
        OnPropertyChanged(nameof(PanelTextOpacityFraction));
        ApplyIfNotLoading();
    }
    partial void OnPanelBaseOpacityChanged(int value)
    {
        OnPropertyChanged(nameof(PanelBaseOpacityFraction));
        ApplyIfNotLoading();
    }
    partial void OnOverlayOpacityChanged(int value)
    {
        OnPropertyChanged(nameof(OverlayOpacityFraction));
        ApplyIfNotLoading();
    }
    partial void OnEndActionChanged(TimerEndAction value)
    {
        OnPropertyChanged(nameof(IsOverlayAction));
        ApplyIfNotLoading();
    }

    [RelayCommand]
    private void StartTimer()
    {
        if (!TryValidateNumericSettings(out var message))
        {
            ValidationMessage = message;
            return;
        }

        ValidationMessage = string.Empty;
        _service.Start(TimeSpan.FromSeconds(TotalSeconds));
        RefreshState();
    }

    [RelayCommand]
    private void PauseTimer()
    {
        if (_service.IsRunning)
        {
            _service.Pause();
        }
        else
        {
            _service.Resume();
        }

        RefreshState();
    }

    [RelayCommand]
    private void StopTimer()
    {
        _service.Stop();
        RefreshState();
    }

    /// <summary>面板拖拽结束后由窗口调用：夹取到虚拟屏幕内并持久化。</summary>
    public void SavePanelPosition(double left, double top)
    {
        var settings = _runtime.Settings;
        settings.TimerPanelLeft = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - PanelWidth);
        settings.TimerPanelTop = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40);
        _ = PersistAsync();
    }

    private void OnServiceChanged()
    {
        _dispatcher.BeginInvoke(RefreshState);
    }

    private void RefreshState()
    {
        IsTimerActive = _service.IsActive;
        IsTimerPaused = _service.IsPaused;
        RemainingText = FormatRemaining(_service.Remaining);
        UpdateFontSizes();
        UpdatePanelVisibility();
    }

    private static string FormatRemaining(TimeSpan remaining) =>
        remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
            : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";

    /// <summary>按可用宽度实测剩余时间文本宽度反推字号（FormattedText），夹在 10–32；窄面板同时收紧内边距。</summary>
    private void UpdateFontSizes()
    {
        var horizontal = PanelWidth < 120 ? 8 : 14;
        var padding = new Thickness(horizontal, 10, horizontal, 10);
        if (padding != _panelPadding)
        {
            _panelPadding = padding;
            OnPropertyChanged(nameof(PanelPadding));
        }

        var available = PanelWidth - horizontal * 2;
        var size = Math.Clamp(available / MeasureRemainingEm(), 10, 32);
        var status = Math.Clamp(size * 0.42, 9, 13);
        if (Math.Abs(size - _panelFontSize) > 0.05)
        {
            _panelFontSize = size;
            OnPropertyChanged(nameof(PanelFontSize));
        }

        if (Math.Abs(status - _panelStatusFontSize) > 0.05)
        {
            _panelStatusFontSize = status;
            OnPropertyChanged(nameof(PanelStatusFontSize));
        }
    }

    /// <summary>剩余时间文本在 100px 字号下的实测宽度换算成 em；与面板数字 TextBlock 的字体/字重一致。</summary>
    private double MeasureRemainingEm()
    {
        var text = string.IsNullOrEmpty(RemainingText) ? "00:00" : RemainingText;
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            PanelDigitsTypeface,
            100,
            Brushes.Black,
            1);
        return Math.Max(formatted.WidthIncludingTrailingWhitespace / 100, 0.1);
    }

    private void UpdatePanelVisibility()
    {
        if (ShowPanel && _service.IsActive)
        {
            if (_panel is null)
            {
                _panel = new Views.CountdownPanelWindow { DataContext = this };
            }

            if (!_panel.IsVisible)
            {
                _panel.Show();
            }
        }
        else
        {
            _panel?.Hide();
        }
    }

    /// <summary>加载期间不触发即时生效，避免逐属性回写。</summary>
    private void ApplyIfNotLoading()
    {
        if (_isLoading)
        {
            return;
        }

        if (!TryValidateNumericSettings(out var message))
        {
            ValidationMessage = message;
            return;
        }

        ValidationMessage = string.Empty;
        var settings = _runtime.Settings;
        settings.TimerDurationSeconds = TotalSeconds;
        settings.TimerShowPanel = ShowPanel;
        settings.TimerPanelWidth = Math.Clamp(PanelWidth, 60, 480);
        settings.TimerPanelTextOpacity = Math.Clamp(PanelTextOpacity, 20, 100);
        settings.TimerPanelBaseOpacity = Math.Clamp(PanelBaseOpacity, 0, 100);
        settings.TimerOverlayOpacity = Math.Clamp(OverlayOpacity, 10, 100);
        settings.TimerEndAction = EndAction;
        _ = PersistAsync();
        UpdatePanelVisibility();
    }

    private bool TryValidateNumericSettings(out string message)
    {
        var invalidKey = DurationHours is < 0 or > 99 ? "Loc.Settings.Range.TimerHours"
            : DurationMinutes is < 0 or > 59 ? "Loc.Settings.Range.TimerMinutes"
            : DurationSeconds is < 0 or > 59 ? "Loc.Settings.Range.TimerSeconds"
            : TotalSeconds < 1 ? "Loc.Settings.Range.TimerTotal"
            : PanelWidth is < 60 or > 480 ? "Loc.Settings.Range.TimerPanelWidth"
            : PanelTextOpacity is < 20 or > 100 ? "Loc.Settings.Range.TimerTextOpacity"
            : PanelBaseOpacity is < 0 or > 100 ? "Loc.Settings.Range.TimerBaseOpacity"
            : OverlayOpacity is < 10 or > 100 ? "Loc.Settings.Range.TimerOverlayOpacity"
            : null;
        message = invalidKey is null
            ? string.Empty
            : LocalizationService.T("Loc.Settings.InvalidNumber", LocalizationService.T(invalidKey));
        return invalidKey is null;
    }

    private static IReadOnlyList<OptionItem> BuildEndActionOptions() =>
    [
        new(TimerEndAction.LockScreen, LocalizationService.T("Loc.Timer.Action.LockScreen")),
        new(TimerEndAction.ScreenOverlay, LocalizationService.T("Loc.Timer.Action.ScreenOverlay")),
    ];

    private async Task PersistAsync()
    {
        try
        {
            await _runtime.SaveSettingsAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Debug($"定时器设置保存失败：{exception.Message}");
        }
    }

    private void Load()
    {
        _isLoading = true;
        try
        {
            var settings = _runtime.Settings;
            // 旧配置一次性迁移：总分钟 → 总秒；整窗不透明度 → 文字/底板不透明度。
            // 旧 JSON 不含 TimerDurationSeconds，反序列化会落到模型默认值（非 0），
            // 故只能以「旧字段 > 0」判定旧配置；新配置旧字段恒 0，不会误迁移。
            if (settings.TimerDurationMinutes > 0)
            {
                settings.TimerDurationSeconds = Math.Clamp(settings.TimerDurationMinutes, 1, 600) * 60;
                settings.TimerDurationMinutes = 0;
            }

            if (settings.TimerPanelOpacity > 0)
            {
                settings.TimerPanelTextOpacity = Math.Clamp(settings.TimerPanelOpacity, 20, 100);
                settings.TimerPanelBaseOpacity = Math.Clamp(settings.TimerPanelOpacity, 0, 100);
                settings.TimerPanelOpacity = 0;
            }

            var total = Math.Clamp(settings.TimerDurationSeconds, 1, MaxDurationSeconds);
            DurationHours = total / 3600;
            DurationMinutes = total % 3600 / 60;
            DurationSeconds = total % 60;
            ShowPanel = settings.TimerShowPanel;
            PanelWidth = Math.Clamp(settings.TimerPanelWidth, 60, 480);
            PanelTextOpacity = Math.Clamp(settings.TimerPanelTextOpacity, 20, 100);
            PanelBaseOpacity = Math.Clamp(settings.TimerPanelBaseOpacity, 0, 100);
            OverlayOpacity = Math.Clamp(settings.TimerOverlayOpacity, 10, 100);
            EndAction = settings.TimerEndAction;
        }
        finally
        {
            _isLoading = false;
        }
    }
}