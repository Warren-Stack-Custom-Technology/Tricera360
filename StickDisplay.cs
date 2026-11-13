using System;
using System.Windows;
using System.Windows.Media;

namespace Tricera360;

public sealed class StickDisplay : FrameworkElement
{
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
        AppTheme theme = AppTheme.Current;
        double radius = Math.Max(1, Math.Min(ActualHeight, ActualWidth) / 2 - 7);
        Point center = new(ActualWidth / 2, ActualHeight / 2);
        drawing.DrawEllipse(theme.StickFace, theme.StickOutline, center, radius, radius);
        drawing.DrawLine(theme.StickGrid, new(center.X - radius, center.Y), new(center.X + radius, center.Y));
        drawing.DrawLine(theme.StickGrid, new(center.X, center.Y - radius), new(center.X, center.Y + radius));
        double limit = Math.Max(1, Math.Sqrt(_x * _x + _y * _y));
        Point position = new(center.X + _x / limit * radius, center.Y + _y / limit * radius);
        drawing.DrawEllipse(_live ? theme.Accent : theme.StickQuietDot, null, position, 5.5, 5.5);
    }
}
