using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Controls;

/// <summary>
/// 图表图例。列出全部系列的颜色与名称；鼠标悬停某一项时把系列键写回
/// <see cref="HighlightedSeriesKey"/>，与柱状图形成双向高亮。
/// </summary>
public sealed class ChartLegend : ItemsControl
{
    static ChartLegend()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ChartLegend),
            new FrameworkPropertyMetadata(typeof(ChartLegend)));
    }

    public static readonly DependencyProperty HighlightedSeriesKeyProperty =
        DependencyProperty.Register(
            nameof(HighlightedSeriesKey),
            typeof(string),
            typeof(ChartLegend),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnHighlightChanged));

    public string? HighlightedSeriesKey
    {
        get => (string?)GetValue(HighlightedSeriesKeyProperty);
        set => SetValue(HighlightedSeriesKeyProperty, value);
    }

    protected override DependencyObject GetContainerForItemOverride() => new ChartLegendItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is ChartLegendItem;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ChartLegendItem container)
        {
            container.Sync(HighlightedSeriesKey);
        }
    }

    private static void OnHighlightChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (sender is not ChartLegend legend)
        {
            return;
        }

        var key = (string?)eventArgs.NewValue;
        foreach (var item in legend.Items)
        {
            if (legend.ItemContainerGenerator.ContainerFromItem(item) is ChartLegendItem container)
            {
                container.Sync(key);
            }
        }
    }
}

/// <summary>图例中的一项。鼠标进出时更新宿主图例的高亮系列。</summary>
public sealed class ChartLegendItem : ContentControl
{
    static ChartLegendItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ChartLegendItem),
            new FrameworkPropertyMetadata(typeof(ChartLegendItem)));
    }

    /// <summary>本项是否处于高亮态；未高亮任何项时全部为真，避免整排变灰。</summary>
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(ChartLegendItem),
            new FrameworkPropertyMetadata(true));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        private set => SetValue(IsActiveProperty, value);
    }

    protected override void OnMouseEnter(MouseEventArgs eventArgs)
    {
        base.OnMouseEnter(eventArgs);
        if (Owner is { } legend && Content is ChartSeries series)
        {
            legend.HighlightedSeriesKey = series.Key;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (Owner is { } legend)
        {
            legend.HighlightedSeriesKey = null;
        }
    }

    private ChartLegend? Owner => ItemsControl.ItemsControlFromItemContainer(this) as ChartLegend;

    internal void Sync(string? highlightedKey) =>
        IsActive = string.IsNullOrEmpty(highlightedKey) ||
                   (Content is ChartSeries series &&
                    string.Equals(series.Key, highlightedKey, StringComparison.Ordinal));
}
