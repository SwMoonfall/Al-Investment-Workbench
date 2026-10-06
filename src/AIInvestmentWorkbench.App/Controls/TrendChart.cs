using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AIInvestmentWorkbench.App.ViewModels;
namespace AIInvestmentWorkbench.App.Controls;

/// <summary>Small, dependency-free chart. Missing periods break the line; values retain their signs.</summary>
public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<TrendPoint>), typeof(TrendChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(TrendChart), new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(TrendChart), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsPercentProperty = DependencyProperty.Register(nameof(IsPercent), typeof(bool), typeof(TrendChart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<TrendPoint>? Points { get => (IReadOnlyList<TrendPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public bool IsPercent { get => (bool)GetValue(IsPercentProperty); set => SetValue(IsPercentProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var items = Points?.ToArray() ?? [];
        void Text(string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 11, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        if (items.Length == 0 || items.All(x => x.Value is null)) { Text("尚无可比较数据", 10, 45); return; }
        var values = items.Where(x => x.Value.HasValue).Select(x => (double)x.Value!.Value).ToArray();
        var low = Math.Min(0, values.Min()); var high = Math.Max(0, values.Max()); if (high == low) high = low + 1;
        var left = 70d; var width = Math.Max(10, ActualWidth - left - 40); var height = Math.Max(10, ActualHeight - 55);
        double Y(double v) => 10 + (high - v) / (high - low) * height;
        string Format(double v) => IsPercent ? v.ToString("P0", CultureInfo.CurrentCulture) : v.ToString("0.##E+0", CultureInfo.CurrentCulture);
        Text(Format(high), 0, 4); Text(Format(low), 0, height + 5);
        dc.DrawLine(new Pen(TextBrush, .5), new(left, Y(0)), new(left + width, Y(0)));
        Point? previous = null;
        for (var i = 0; i < items.Length; i++)
        {
            var x = left + (items.Length == 1 ? width / 2 : i * width / (items.Length - 1));
            if (i == 0 || i == items.Length - 1 || items.Length <= 4) Text(items[i].Period, x - 18, height + 25);
            if (items[i].Value is not { } value) { previous = null; continue; }
            var point = new Point(x, Y((double)value));
            if (previous.HasValue) dc.DrawLine(new Pen(Stroke, 2.5), previous.Value, point);
            dc.DrawEllipse(Stroke, null, point, 3.5, 3.5); previous = point;
        }
    }
}
