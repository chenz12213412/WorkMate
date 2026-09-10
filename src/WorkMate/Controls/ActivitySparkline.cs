using System.Windows;
using System.Windows.Media;
using WorkMate.Models;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace WorkMate.Controls;

public sealed class ActivitySparkline : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IReadOnlyList<ActivitySeriesPoint>),
        typeof(ActivitySparkline),
        new FrameworkPropertyMetadata(
            Array.Empty<ActivitySeriesPoint>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ActivitySeriesPoint> ItemsSource
    {
        get => (IReadOnlyList<ActivitySeriesPoint>)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(223, 233, 242)), 1);
        gridPen.Freeze();
        for (var index = 1; index <= 2; index++)
        {
            var y = ActualHeight * index / 3d;
            drawingContext.DrawLine(gridPen, new WpfPoint(0, y), new WpfPoint(ActualWidth, y));
        }

        if (ItemsSource.Count < 2)
        {
            var emptyPen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(185, 203, 219)), 1);
            emptyPen.Freeze();
            drawingContext.DrawLine(
                emptyPen,
                new WpfPoint(0, ActualHeight - 1),
                new WpfPoint(ActualWidth, ActualHeight - 1));
            return;
        }

        var start = ItemsSource[0].Time;
        var end = ItemsSource[^1].Time;
        var duration = Math.Max(1, (end - start).TotalSeconds);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < ItemsSource.Count; index++)
            {
                var item = ItemsSource[index];
                var x = (item.Time - start).TotalSeconds / duration * ActualWidth;
                var normalized = Math.Clamp(item.Value, 0, 100) / 100d;
                var y = ActualHeight - (normalized * (ActualHeight - 2)) - 1;
                var point = new WpfPoint(x, y);
                if (index == 0)
                {
                    context.BeginFigure(point, isFilled: false, isClosed: false);
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: true);
                }
            }
        }

        geometry.Freeze();
        var linePen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(23, 110, 219)), 1.6)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        linePen.Freeze();
        drawingContext.DrawGeometry(null, linePen, geometry);
    }
}
