using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AppUsageTracker.ViewModels;

namespace AppUsageTracker.Controls;

/// <summary>
/// 可缩放堆叠柱状图。横轴为槽位（时间片或日期），每根柱按软件堆叠，
/// 支持滚轮以鼠标为锚点缩放、按住拖拽平移、双击复位。
/// 悬停柱段或图例项时高亮同一软件，并在柱段上给出简略提示。
/// 绘制走 <see cref="OnRender"/> 直接出几何，避免为成百上千个柱段创建可视元素。
/// </summary>
public sealed class ZoomableBarChart : FrameworkElement
{
    private const double PlotTopPadding = 10d;
    private const double AxisHeight = 20d;
    /// <summary>纵轴刻度占用的左侧宽度，绘图区从这里开始。</summary>
    private const double AxisGutter = 34d;
    private const double AxisLabelGap = 3d;
    private const double BarGap = 2d;
    private const double SegmentGap = 2d;
    private const double CornerRadius = 4d;
    private const double DimOpacity = 0.22d;
    private const double GridLineCount = 4d;

    private static readonly Typeface LabelTypeface = new("Microsoft YaHei UI");

    private readonly ToolTip _tooltip = new()
    {
        Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
        HasDropShadow = true,
    };

    private readonly List<SlotHit> _hits = [];
    private double _zoom = 1d;
    private double _offset;
    private Point? _dragOrigin;
    private double _dragOriginOffset;
    private int _hoverSlot = -1;
    private string? _hoverSeriesKey;

    public ZoomableBarChart()
    {
        Focusable = true;
        ClipToBounds = true;
        ToolTip = _tooltip;
        ToolTipService.SetInitialShowDelay(this, 120);
        ToolTipService.SetShowDuration(this, 60000);
        ToolTipService.SetBetweenShowDelay(this, 0);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(ChartData),
            typeof(ZoomableBarChart),
            new FrameworkPropertyMetadata(
                ChartData.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnDataChanged));

    /// <summary>图表数据。整体替换，替换后保持当前缩放与平移。</summary>
    public ChartData Data
    {
        get => (ChartData)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>被悬停的系列键。由控件写入、图例读取，实现柱段与图例的双向高亮。</summary>
    public static readonly DependencyProperty HighlightedSeriesKeyProperty =
        DependencyProperty.Register(
            nameof(HighlightedSeriesKey),
            typeof(string),
            typeof(ZoomableBarChart),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault |
                FrameworkPropertyMetadataOptions.AffectsRender));

    public string? HighlightedSeriesKey
    {
        get => (string?)GetValue(HighlightedSeriesKeyProperty);
        set => SetValue(HighlightedSeriesKeyProperty, value);
    }

    /// <summary>横轴与网格线颜色，由页面按主题传入。</summary>
    public static readonly DependencyProperty AxisBrushProperty =
        DependencyProperty.Register(
            nameof(AxisBrush),
            typeof(Brush),
            typeof(ZoomableBarChart),
            new FrameworkPropertyMetadata(
                Brushes.Gainsboro,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush AxisBrush
    {
        get => (Brush)GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    /// <summary>刻度文字颜色。</summary>
    public static readonly DependencyProperty LabelBrushProperty =
        DependencyProperty.Register(
            nameof(LabelBrush),
            typeof(Brush),
            typeof(ZoomableBarChart),
            new FrameworkPropertyMetadata(
                Brushes.Gray,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    /// <summary>柱段之间的缝隙用的画刷，取卡片背景色，让相邻色块靠留白分隔。</summary>
    public static readonly DependencyProperty SurfaceBrushProperty =
        DependencyProperty.Register(
            nameof(SurfaceBrush),
            typeof(Brush),
            typeof(ZoomableBarChart),
            new FrameworkPropertyMetadata(
                Brushes.White,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush SurfaceBrush
    {
        get => (Brush)GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    /// <summary>当前缩放倍数，供页面显示“1.0x”之类的提示。</summary>
    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel),
            typeof(double),
            typeof(ZoomableBarChart),
            new FrameworkPropertyMetadata(1d));

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        private set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>复位缩放和平移。</summary>
    public void ResetView()
    {
        _zoom = 1d;
        _offset = 0d;
        ZoomLevel = 1d;
        InvalidateVisual();
    }

    private static void OnDataChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (sender is ZoomableBarChart chart)
        {
            // 数据集长度变化后原偏移可能越界，重新夹一次。
            chart._offset = ChartLayout.ClampOffset(chart._offset, chart._zoom);
            chart._hoverSlot = -1;
            chart.HideTooltip();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        _hits.Clear();

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // 背景必须有实体填充，否则鼠标事件不会命中空白区域。
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var data = Data ?? ChartData.Empty;
        var plotHeight = Math.Max(0d, height - AxisHeight);
        // 左侧留出纵轴刻度的宽度，柱子从刻度右边开始画，避免数字压在柱子上。
        var plotLeft = AxisGutter;
        var plotWidth = Math.Max(0d, width - plotLeft);
        var axisPen = new Pen(AxisBrush, 1d);
        axisPen.Freeze();

        context.DrawLine(
            axisPen,
            new Point(plotLeft, Snap(plotHeight)),
            new Point(width, Snap(plotHeight)));

        if (data.Slots.Count == 0 || plotHeight <= PlotTopPadding || plotWidth <= 0)
        {
            return;
        }

        var grouping = ChartLayout.ComputeGrouping(
            data.Slots.Count,
            _zoom,
            plotWidth,
            data.GroupingSteps);
        var groupCount = (int)Math.Ceiling(data.Slots.Count / (double)grouping);
        var groupWidth = plotWidth * _zoom / groupCount;
        var originX = plotLeft - _offset * plotWidth * _zoom;

        var maxSeconds = MaxGroupSeconds(data, grouping, groupCount);
        if (maxSeconds <= 0)
        {
            return;
        }

        DrawGrid(context, data, maxSeconds, plotLeft, width, plotHeight, axisPen);

        var seriesLookup = data.Series.ToDictionary(item => item.Key);
        var scale = (plotHeight - PlotTopPadding) / maxSeconds;
        var (firstGroup, lastGroup) = ChartLayout.VisibleRange(groupCount, _zoom, _offset);

        // 绘图区之外的部分不画，否则柱子会盖住纵轴刻度。
        context.PushClip(new RectangleGeometry(new Rect(plotLeft, 0, plotWidth, plotHeight)));
        for (var group = firstGroup; group < lastGroup; group++)
        {
            var slotLeft = originX + group * groupWidth;
            var barWidth = Math.Min(ChartLayout.MaxBarWidth, Math.Max(1d, groupWidth - BarGap));
            var barLeft = slotLeft + (groupWidth - barWidth) / 2d;
            if (barLeft > width || barLeft + barWidth < plotLeft)
            {
                continue;
            }

            var totals = AggregateGroup(data, grouping, group);
            var groupSeconds = totals.Values.Sum();
            if (groupSeconds <= 0)
            {
                continue;
            }

            var barTop = plotHeight - groupSeconds * scale;
            _hits.Add(new SlotHit(group, barLeft, barLeft + barWidth, barTop, plotHeight, totals));
            DrawStack(context, data, seriesLookup, totals, barLeft, barWidth, plotHeight, scale);
        }

        context.Pop();
        DrawAxisLabels(
            context,
            data,
            grouping,
            groupCount,
            groupWidth,
            originX,
            plotLeft,
            width,
            plotHeight);
    }

    private void DrawStack(
        DrawingContext context,
        ChartData data,
        IReadOnlyDictionary<string, ChartSeries> seriesLookup,
        IReadOnlyDictionary<string, long> totals,
        double barLeft,
        double barWidth,
        double baseline,
        double scale)
    {
        // 自下而上按系列顺序堆叠，保证同一软件在所有柱子里的层序一致。
        var cursor = baseline;
        var isFirstSegment = true;
        for (var index = data.Series.Count - 1; index >= 0; index--)
        {
            var series = data.Series[index];
            if (!totals.TryGetValue(series.Key, out var seconds) || seconds <= 0)
            {
                continue;
            }

            var segmentHeight = seconds * scale;
            var top = cursor - segmentHeight;
            var brush = SegmentBrush(series);

            // 顶端一段做圆角，其余保持直角，让整根柱子读起来是一个整体。
            if (isFirstSegment)
            {
                context.DrawGeometry(
                    brush,
                    null,
                    RoundedTopGeometry(barLeft, top, barWidth, segmentHeight));
                isFirstSegment = false;
            }
            else
            {
                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(barLeft, top, barWidth, segmentHeight));
            }

            // 相邻色块靠背景色缝隙分隔，而不是描边。
            if (cursor < baseline - 0.5 && segmentHeight > SegmentGap * 2)
            {
                context.DrawRectangle(
                    SurfaceBrush,
                    null,
                    new Rect(barLeft, cursor - SegmentGap, barWidth, SegmentGap));
            }

            cursor = top;
            _ = seriesLookup;
        }
    }

    private Brush SegmentBrush(ChartSeries series)
    {
        var highlight = HighlightedSeriesKey;
        var dimmed = !string.IsNullOrEmpty(highlight) &&
                     !string.Equals(highlight, series.Key, StringComparison.Ordinal);
        var brush = new SolidColorBrush(series.Color)
        {
            Opacity = dimmed ? DimOpacity : 1d,
        };
        brush.Freeze();
        return brush;
    }

    private static Geometry RoundedTopGeometry(
        double left,
        double top,
        double width,
        double height)
    {
        var radius = Math.Min(CornerRadius, Math.Min(width / 2d, height));
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(new Point(left, top + height), true, true);
            stream.LineTo(new Point(left, top + radius), true, false);
            stream.ArcTo(
                new Point(left + radius, top),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                false);
            stream.LineTo(new Point(left + width - radius, top), true, false);
            stream.ArcTo(
                new Point(left + width, top + radius),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                false);
            stream.LineTo(new Point(left + width, top + height), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private void DrawGrid(
        DrawingContext context,
        ChartData data,
        long maxSeconds,
        double plotLeft,
        double width,
        double plotHeight,
        Pen axisPen)
    {
        var usable = plotHeight - PlotTopPadding;
        for (var step = 1; step <= GridLineCount; step++)
        {
            var ratio = step / GridLineCount;
            var y = Snap(plotHeight - usable * ratio);
            context.DrawLine(axisPen, new Point(plotLeft, y), new Point(width, y));

            var value = data.ToAxisValue((long)(maxSeconds * ratio));
            var text = BuildText(
                value >= 10
                    ? value.ToString("F0", CultureInfo.InvariantCulture)
                    : value.ToString("0.#", CultureInfo.InvariantCulture),
                10d,
                LabelBrush);

            // 刻度数字右对齐贴住绘图区左边界。
            context.DrawText(
                text,
                new Point(plotLeft - text.Width - 5, y - text.Height / 2d));
        }

        // 纵轴单位标在顶端，省得每个刻度都带单位。
        var unitLabel = data.Unit == ChartValueUnit.Hours
            ? AppUsageTracker.Services.LocalizationService.T("Loc.Unit.Hours")
            : AppUsageTracker.Services.LocalizationService.T("Loc.Unit.Minutes");
        var unit = BuildText(unitLabel, 10d, LabelBrush);
        context.DrawText(unit, new Point(plotLeft - unit.Width - 5, 0));
    }

    private void DrawAxisLabels(
        DrawingContext context,
        ChartData data,
        int grouping,
        int groupCount,
        double groupWidth,
        double originX,
        double plotLeft,
        double width,
        double plotHeight)
    {
        var step = ChartLayout.ComputeTickStep((int)Math.Ceiling(groupCount / _zoom));
        var lastRight = double.NegativeInfinity;

        for (var group = 0; group < groupCount; group += step)
        {
            var slotIndex = group * grouping;
            if (slotIndex >= data.Slots.Count)
            {
                break;
            }

            var center = originX + group * groupWidth + groupWidth / 2d;
            if (center < plotLeft - 40 || center > width + 40)
            {
                continue;
            }

            var text = BuildText(data.Slots[slotIndex].Label, 10.5d, LabelBrush);

            // 标签不越过绘图区左右边界，否则首尾会被裁掉半个。
            var left = Math.Clamp(
                center - text.Width / 2d,
                plotLeft,
                Math.Max(plotLeft, width - text.Width));
            if (left < lastRight + 6)
            {
                continue;
            }

            lastRight = left + text.Width;
            context.DrawText(text, new Point(left, plotHeight + AxisLabelGap));
        }
    }

    private FormattedText BuildText(string value, double size, Brush brush) =>
        new(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static double Snap(double value) => Math.Round(value) + 0.5d;

    private static Dictionary<string, long> AggregateGroup(
        ChartData data,
        int grouping,
        int group)
    {
        if (grouping == 1)
        {
            return data.Slots[group].Values;
        }

        var totals = new Dictionary<string, long>();
        var start = group * grouping;
        var end = Math.Min(data.Slots.Count, start + grouping);
        for (var index = start; index < end; index++)
        {
            foreach (var pair in data.Slots[index].Values)
            {
                totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + pair.Value;
            }
        }

        return totals;
    }

    private static long MaxGroupSeconds(ChartData data, int grouping, int groupCount)
    {
        if (grouping == 1)
        {
            return data.Slots.Count == 0 ? 0 : data.Slots.Max(item => item.TotalSeconds);
        }

        var max = 0L;
        for (var group = 0; group < groupCount; group++)
        {
            var start = group * grouping;
            var end = Math.Min(data.Slots.Count, start + grouping);
            var sum = 0L;
            for (var index = start; index < end; index++)
            {
                sum += data.Slots[index].TotalSeconds;
            }

            max = Math.Max(max, sum);
        }

        return max;
    }

    /// <summary>绘图区宽度，不含左侧纵轴刻度占用的部分。</summary>
    private double PlotWidth => Math.Max(0d, ActualWidth - AxisGutter);

    protected override void OnMouseWheel(MouseWheelEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        if (PlotWidth <= 0 || Data.Slots.Count == 0)
        {
            return;
        }

        var anchor = (eventArgs.GetPosition(this).X - AxisGutter) / PlotWidth;
        var factor = eventArgs.Delta > 0 ? 1.25d : 1d / 1.25d;
        (_zoom, _offset) = ChartLayout.ZoomAt(_zoom, _offset, anchor, factor);
        ZoomLevel = _zoom;
        eventArgs.Handled = true;
        InvalidateVisual();
        UpdateHover(eventArgs.GetPosition(this));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonDown(eventArgs);
        if (eventArgs.ClickCount == 2)
        {
            ResetView();
            eventArgs.Handled = true;
            return;
        }

        // 未放大时没有可平移的空间，保持默认光标。
        if (_zoom <= 1d)
        {
            return;
        }

        _dragOrigin = eventArgs.GetPosition(this);
        _dragOriginOffset = _offset;
        CaptureMouse();
        Cursor = Cursors.ScrollWE;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonUp(eventArgs);
        EndDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs eventArgs)
    {
        base.OnLostMouseCapture(eventArgs);
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragOrigin is null)
        {
            return;
        }

        _dragOrigin = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        var position = eventArgs.GetPosition(this);

        if (_dragOrigin is { } origin && PlotWidth > 0)
        {
            var deltaRatio = (position.X - origin.X) / (PlotWidth * _zoom);
            _offset = ChartLayout.ClampOffset(_dragOriginOffset - deltaRatio, _zoom);
            InvalidateVisual();
            return;
        }

        UpdateHover(position);
    }

    protected override void OnMouseLeave(MouseEventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        _hoverSlot = -1;
        HighlightedSeriesKey = null;
        _hoverSeriesKey = null;
        HideTooltip();
        InvalidateVisual();
    }

    private void UpdateHover(Point position)
    {
        var hit = _hits.FirstOrDefault(item =>
            position.X >= item.Left &&
            position.X <= item.Right &&
            position.Y >= item.Top - 4 &&
            position.Y <= item.Baseline);

        if (hit is null)
        {
            if (_hoverSlot != -1 || _hoverSeriesKey is not null)
            {
                _hoverSlot = -1;
                _hoverSeriesKey = null;
                HighlightedSeriesKey = null;
                HideTooltip();
                InvalidateVisual();
            }

            return;
        }

        var seriesKey = SeriesAt(hit, position.Y);
        if (hit.Group == _hoverSlot &&
            string.Equals(seriesKey, _hoverSeriesKey, StringComparison.Ordinal))
        {
            MoveTooltip(position);
            return;
        }

        _hoverSlot = hit.Group;
        _hoverSeriesKey = seriesKey;
        HighlightedSeriesKey = seriesKey;
        ShowTooltip(hit, seriesKey, position);
        InvalidateVisual();
    }

    /// <summary>根据纵坐标反查鼠标落在哪一段柱段上。</summary>
    private string? SeriesAt(SlotHit hit, double y)
    {
        var data = Data;
        var totalSeconds = hit.Totals.Values.Sum();
        if (totalSeconds <= 0)
        {
            return null;
        }

        var scale = (hit.Baseline - hit.Top) / totalSeconds;
        var cursor = hit.Baseline;
        for (var index = data.Series.Count - 1; index >= 0; index--)
        {
            var series = data.Series[index];
            if (!hit.Totals.TryGetValue(series.Key, out var seconds) || seconds <= 0)
            {
                continue;
            }

            var top = cursor - seconds * scale;
            if (y >= top && y <= cursor)
            {
                return series.Key;
            }

            cursor = top;
        }

        return null;
    }

    private void ShowTooltip(SlotHit hit, string? seriesKey, Point position)
    {
        var data = Data;
        var startSlot = hit.Group * GroupingForHit(hit);
        if (startSlot >= data.Slots.Count)
        {
            return;
        }

        var endSlot = Math.Min(data.Slots.Count - 1, startSlot + GroupingForHit(hit) - 1);
        var range = data.Slots[startSlot].RangeStart == data.Slots[endSlot].RangeEnd
            ? data.Slots[startSlot].RangeStart
            : $"{data.Slots[startSlot].RangeStart} - {data.Slots[endSlot].RangeEnd}";

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = range,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // 只列出该槽位里时长靠前的几项，避免提示条被长列表撑爆。
        var rows = data.Series
            .Where(series => hit.Totals.GetValueOrDefault(series.Key) > 0)
            .OrderByDescending(series => hit.Totals[series.Key])
            .Take(6)
            .ToList();
        foreach (var series in rows)
        {
            var isHovered = string.Equals(series.Key, seriesKey, StringComparison.Ordinal);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 9,
                Height = 9,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(series.Color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            row.Children.Add(new TextBlock
            {
                Text = series.Name,
                MaxWidth = 180,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = isHovered ? FontWeights.SemiBold : FontWeights.Normal,
            });
            row.Children.Add(new TextBlock
            {
                Text = DurationFormatter.Format(hit.Totals[series.Key]),
                Margin = new Thickness(10, 0, 0, 0),
                FontWeight = isHovered ? FontWeights.SemiBold : FontWeights.Normal,
            });
            row.Margin = new Thickness(0, 1, 0, 1);
            panel.Children.Add(row);
        }

        var hidden = data.Series.Count(series => hit.Totals.GetValueOrDefault(series.Key) > 0)
                     - rows.Count;
        if (hidden > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = AppUsageTracker.Services.LocalizationService.T("Loc.Chart.MoreItems", hidden),
                Opacity = 0.7,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = AppUsageTracker.Services.LocalizationService.T(
                "Loc.Chart.Total",
                DurationFormatter.Format(hit.Totals.Values.Sum())),
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = 0.75,
        });

        _tooltip.Content = panel;
        MoveTooltip(position);
        _tooltip.IsOpen = true;
    }

    private int GroupingForHit(SlotHit hit)
    {
        _ = hit;
        return ChartLayout.ComputeGrouping(
            Data.Slots.Count,
            _zoom,
            PlotWidth,
            Data.GroupingSteps);
    }

    private void MoveTooltip(Point position)
    {
        _tooltip.PlacementTarget = this;
        _tooltip.HorizontalOffset = position.X + 16;
        _tooltip.VerticalOffset = position.Y + 16;
    }

    private void HideTooltip() => _tooltip.IsOpen = false;

    /// <summary>一根已绘制柱子的命中信息，用于悬停反查。</summary>
    private sealed record SlotHit(
        int Group,
        double Left,
        double Right,
        double Top,
        double Baseline,
        IReadOnlyDictionary<string, long> Totals);
}
