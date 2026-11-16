using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AppUsageTracker.Services;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Tests;

/// <summary>定时器页设置：时分秒合成、范围校验、透明度拆分、旧配置迁移与窄面板自适应字号。</summary>
public sealed class TimerViewModelTests
{
    [Fact]
    public void DurationComposesHoursMinutesSeconds()
    {
        var vm = Create(out var runtime);

        vm.DurationHours = 1;
        vm.DurationMinutes = 2;
        vm.DurationSeconds = 3;

        Assert.Equal(3723, runtime.Settings.TimerDurationSeconds);
        Assert.Empty(vm.ValidationMessage);
    }

    [Fact]
    public void ZeroTotalDurationIsRejected()
    {
        var vm = Create(out var runtime);

        vm.DurationHours = 0;
        vm.DurationMinutes = 0;
        vm.DurationSeconds = 0;

        Assert.NotEmpty(vm.ValidationMessage);
        Assert.Equal(1500, runtime.Settings.TimerDurationSeconds);
    }

    [Fact]
    public void OutOfRangeMinuteIsRejected()
    {
        var vm = Create(out var runtime);

        vm.DurationMinutes = 60;

        Assert.NotEmpty(vm.ValidationMessage);
        Assert.Equal(1500, runtime.Settings.TimerDurationSeconds);
    }

    [Fact]
    public void NarrowPanelWidthIsAcceptedAndFontShrinks()
    {
        var vm = Create(out var runtime);
        var wideFont = vm.PanelFontSize;

        vm.PanelWidth = 120;
        Assert.Equal(120, runtime.Settings.TimerPanelWidth);
        Assert.Empty(vm.ValidationMessage);

        vm.PanelWidth = 60;
        Assert.Equal(60, runtime.Settings.TimerPanelWidth);
        Assert.InRange(vm.PanelFontSize, 10, wideFont - 1);
    }

    [Fact]
    public void NarrowPanelDigitsFitWithoutWrapping()
    {
        var vm = Create(out _);
        vm.PanelWidth = 60;

        var typeface = new Typeface(
            SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var formatted = new FormattedText(
            vm.RemainingText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, vm.PanelFontSize, Brushes.Black, 1);

        // 60 宽窄面板左右内边距各 8：数字实测宽度必须放得下，超出即换行变形回归
        Assert.True(formatted.WidthIncludingTrailingWhitespace <= 44.5,
            $"text={vm.RemainingText} width={formatted.WidthIncludingTrailingWhitespace:F2} font={vm.PanelFontSize:F2}");
    }

    [Fact]
    public void OpacitySplitPersistsSeparately()
    {
        var vm = Create(out var runtime);

        vm.PanelTextOpacity = 50;
        vm.PanelBaseOpacity = 0;

        Assert.Equal(50, runtime.Settings.TimerPanelTextOpacity);
        Assert.Equal(0, runtime.Settings.TimerPanelBaseOpacity);
        Assert.Equal(0.5, vm.PanelTextOpacityFraction);
        Assert.Equal(0.0, vm.PanelBaseOpacityFraction);
    }

    [Fact]
    public void OverlayOpacityIsRangeChecked()
    {
        var vm = Create(out var runtime);

        vm.OverlayOpacity = 5;

        Assert.NotEmpty(vm.ValidationMessage);
        Assert.Equal(50, runtime.Settings.TimerOverlayOpacity);
    }

    [Fact]
    public void LegacySettingsMigrateOnce()
    {
        var runtime = CreateRuntime();
        runtime.Settings.TimerDurationSeconds = 0;
        runtime.Settings.TimerDurationMinutes = 25;
        runtime.Settings.TimerPanelOpacity = 90;

        var vm = new TimerViewModel(runtime, new CountdownTimerService());

        Assert.Equal(0, vm.DurationHours);
        Assert.Equal(25, vm.DurationMinutes);
        Assert.Equal(0, vm.DurationSeconds);
        Assert.Equal(1500, runtime.Settings.TimerDurationSeconds);
        Assert.Equal(0, runtime.Settings.TimerDurationMinutes);
        Assert.Equal(90, vm.PanelTextOpacity);
        Assert.Equal(90, vm.PanelBaseOpacity);
        Assert.Equal(0, runtime.Settings.TimerPanelOpacity);
    }

    [Fact]
    public void LegacyMinutesMigrateEvenWhenSecondsFallsBackToModelDefault()
    {
        // 真实旧 JSON 不含 TimerDurationSeconds，反序列化落到模型默认 1500（非 0）；
        // 迁移必须仍按旧字段生效，否则用户时长会被默认值覆盖。
        var runtime = CreateRuntime();
        runtime.Settings.TimerDurationSeconds = 1500;
        runtime.Settings.TimerDurationMinutes = 111;

        var vm = new TimerViewModel(runtime, new CountdownTimerService());

        Assert.Equal(6660, runtime.Settings.TimerDurationSeconds);
        Assert.Equal(0, runtime.Settings.TimerDurationMinutes);
        Assert.Equal(1, vm.DurationHours);
        Assert.Equal(51, vm.DurationMinutes);
        Assert.Equal(0, vm.DurationSeconds);
    }

    private static TimerViewModel Create(out AppRuntime runtime)
    {
        runtime = CreateRuntime();
        return new TimerViewModel(runtime, new CountdownTimerService());
    }

    private static AppRuntime CreateRuntime() =>
        new(
            new MemoryAppDataStore(),
            new FakeTimeProvider(new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)),
            new ApplicationMatcher(),
            new FakeProcessScanner());
}