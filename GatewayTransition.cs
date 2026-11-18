using System.Windows;
using System.Windows.Media;

namespace MacDock;

// The four panes are drawn from their original vector outline. Both windows
// receive the same warped drawing, split at the top edge of the Dock HWND.
internal sealed record GatewayPose(double Phase, Geometry? Panes, Brush Ink, double Opacity, double OpenAmount)
{
    static readonly Point[][] PaneCorners =
    {
        new Point[] { new(0,6), new(12,4), new(12,17), new(0,17) },
        new Point[] { new(14,3.6), new(29,1), new(29,17), new(14,17) },
        new Point[] { new(0,19), new(12,19), new(12,32), new(0,29.8) },
        new Point[] { new(14,19), new(29,19), new(29,35), new(14,32.4) }
    };

    internal static GatewayPose At(double phase, double box)
    {
        phase = Math.Clamp(phase, 0, 1);
        var opacity = 1 - GeniePose.Smooth((phase - .045) / .56);
        var open = GeniePose.Smooth((phase - .76) / .24);
        if (opacity <= 0) return new GatewayPose(phase, null, Theme.Ink, 0, open);

        double scale = box / 34;
        double pull = GeniePose.Smooth(phase / .28);
        Point Warp(Point point)
        {
            // The lower ends stay rooted in the button. The higher portions open
            // out and curve toward the live sheet above the anchor.
            double height = (35 - point.Y) / 34;
            double bend = height * height * pull;
            double x = (point.X - 14.5) * (1 + .72 * bend) - 8 * bend;
            double y = point.Y - 18 - 67 * bend;
            return new Point(x * scale, y * scale);
        }

        var path = new StreamGeometry();
        using (var geometry = path.Open())
        {
            foreach (var pane in PaneCorners)
            {
                geometry.BeginFigure(Warp(pane[0]), true, true);
                for (int i = 0; i < pane.Length; i++)
                {
                    Point start = pane[i], end = pane[(i + 1) % pane.Length];
                    var first = Warp(start);
                    var last = Warp(end);
                    var middle = Warp(new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2));
                    var control = new Point(2 * middle.X - (first.X + last.X) / 2, 2 * middle.Y - (first.Y + last.Y) / 2);
                    geometry.QuadraticBezierTo(control, last, true, false);
                }
            }
        }
        path.Freeze();

        Brush ink = Theme.Ink;
        if (phase > 0 && ink is SolidColorBrush solid)
        {
            Color glass = Theme.Dark ? Color.FromRgb(150, 183, 218) : Color.FromRgb(213, 230, 250);
            double blend = GeniePose.Smooth((phase - .08) / .34);
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * blend);
            var brush = new SolidColorBrush(Color.FromRgb(Mix(solid.Color.R, glass.R), Mix(solid.Color.G, glass.G), Mix(solid.Color.B, glass.B)));
            brush.Freeze();
            ink = brush;
        }
        return new GatewayPose(phase, path, ink, opacity, open);
    }
}

internal sealed class GatewayVisual : FrameworkElement
{
    readonly DrawingVisual drawing = new();
    bool hasDrawing;

    internal GatewayVisual()
    {
        IsHitTestVisible = false;
        Focusable = false;
        AddVisualChild(drawing);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => drawing;

    internal void Paint(GatewayPose pose, Point center, Rect? upperClip = null)
    {
        bool panes = pose.Panes != null && (!upperClip.HasValue || center.Y + pose.Panes.Bounds.Top < upperClip.Value.Bottom);
        bool mark = !upperClip.HasValue && pose.OpenAmount > 0;
        if (!panes && !mark)
        {
            Clear();
            return;
        }

        using var context = drawing.RenderOpen();
        if (upperClip is { } clip) context.PushClip(new RectangleGeometry(clip));
        context.PushTransform(new TranslateTransform(center.X, center.Y));
        if (panes)
        {
            context.PushOpacity(pose.Opacity);
            context.DrawGeometry(pose.Ink, null, pose.Panes);
            context.Pop();
        }
        if (mark)
        {
            double width = 12 * pose.OpenAmount;
            context.PushOpacity(.44 * pose.OpenAmount);
            context.DrawRoundedRectangle(Theme.Muted, null, new Rect(-width / 2, -1, width, 2), 1, 1);
            context.Pop();
        }
        context.Pop();
        if (upperClip.HasValue) context.Pop();
        hasDrawing = true;
    }

    internal void Clear()
    {
        if (!hasDrawing) return;
        using (drawing.RenderOpen()) { }
        hasDrawing = false;
    }
}
