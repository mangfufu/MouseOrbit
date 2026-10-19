using System.Globalization;
using System.Windows.Media;

namespace MouseOrbit;

public sealed class PixelCrt : FrameworkElement
{
    public PixelCrt()
    {
        SnapsToDevicePixels = true; RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Loaded += (_, _) => Palette.Changed += InvalidateVisual;
        Unloaded += (_, _) => Palette.Changed -= InvalidateVisual;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth / 100, ActualHeight / 88));
        if (!Palette.Pixel)
        {
            var pen = new Pen(Palette.Edge, 1);
            dc.DrawRoundedRectangle(Palette.Hover, pen, new Rect(9, 12, 67, 46), 5, 5);
            dc.DrawRoundedRectangle(Palette.Surface, pen, new Rect(24, 27, 67, 46), 5, 5);
            dc.DrawLine(new Pen(Palette.Edge, 1), new Point(24, 38), new Point(91, 38));
            dc.DrawLine(new Pen(Palette.Accent, 2), new Point(33, 47), new Point(61, 47));
            dc.DrawLine(new Pen(Palette.Edge, 1), new Point(33, 55), new Point(79, 55));
            dc.DrawLine(new Pen(Palette.Edge, 1), new Point(33, 61), new Point(70, 61));
            dc.Pop(); return;
        }
        void R(double x, double y, double w, double h, Brush b) => dc.DrawRectangle(b, null, new Rect(x, y, w, h));
        var body = Palette.B(Palette.Dark ? "#8B8069" : "#BEB197"); var bright = Palette.B(Palette.Dark ? "#B4A68C" : "#D9CFB7");
        var shadow = Palette.B("#756953"); var screen = Palette.B("#242720"); var screenInk = Palette.B("#A5AE91");
        R(10, 7, 80, 4, bright); R(6, 11, 88, 50, body); R(2, 15, 4, 42, shadow); R(94, 15, 4, 42, shadow);
        R(10, 15, 80, 40, shadow); R(14, 18, 72, 33, screen);
        R(23, 24, 41, 20, screenInk); R(25, 26, 37, 16, screen); R(25, 27, 37, 2, screenInk);
        R(38, 30, 37, 20, screenInk); R(40, 32, 33, 16, screen); R(40, 33, 33, 2, screenInk);
        R(44, 39, 4, 2, Palette.Accent); R(50, 39, 13, 2, shadow); R(44, 43, 19, 1, shadow);
        for (int y = 19; y < 51; y += 4) R(14, y, 72, 1, Palette.B("#15201813"));
        R(10, 57, 80, 6, body); R(15, 59, 17, 2, shadow); R(76, 59, 3, 3, Palette.Accent); R(83, 58, 4, 4, shadow);
        R(39, 63, 22, 9, shadow); R(32, 72, 36, 4, body); R(25, 76, 50, 4, bright); R(21, 80, 58, 3, shadow);
        dc.Pop();
    }
}
