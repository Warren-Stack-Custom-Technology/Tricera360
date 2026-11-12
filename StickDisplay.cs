using System;
using System.Windows;
using System.Windows.Media;

namespace Tricera360;

public sealed class StickDisplay : FrameworkElement
{
    private static readonly Pen Outline = MakePen("#52725C", 1.1);
    private static readonly Pen GridLine = MakePen("#34493D", 1);
    private static readonly Brush Face = MakeBrush("#1D2B22");
    private static readonly Brush Dot = MakeBrush("#64E9EE");
    private static readonly Brush QuietDot = MakeBrush("#83988A");
    private double _x, _y;
    private bool _live;

    internal void Update(double x, double y, bool live)
    {
        if (_x == x && _y == y && _live == live) return;
        (_x, _y, _live) = (x, y, live);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        double radius = Math.Max(1, Math.Min(ActualHeight, ActualWidth) / 2 - 7);
        Point center = new(ActualWidth / 2, ActualHeight / 2);
        drawing.DrawEllipse(Face, Outline, center, radius, radius);
        drawing.DrawLine(GridLine, new(center.X - radius, center.Y), new(center.X + radius, center.Y));
        drawing.DrawLine(GridLine, new(center.X, center.Y - radius), new(center.X, center.Y + radius));
        double limit = Math.Max(1, Math.Sqrt(_x * _x + _y * _y));
        Point position = new(center.X + _x / limit * radius, center.Y + _y / limit * radius);
        drawing.DrawEllipse(_live ? Dot : QuietDot, null, position, 5.5, 5.5);
    }

    private static Brush MakeBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(string color, double width)
    {
        Pen pen = new(MakeBrush(color), width);
        pen.Freeze();
        return pen;
    }
}
