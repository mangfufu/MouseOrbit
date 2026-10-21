using System.Windows.Media;

namespace MouseOrbit;

internal static class SpatialSurfaces
{
    internal static SolidColorBrush Selected
    {
        get
        {
            var surface = Palette.Surface.Color; var ink = Palette.Accent.Color;
            byte Mix(byte a, byte b) => (byte)Math.Round(a * .82 + b * .18);
            var brush = new SolidColorBrush(Color.FromArgb(surface.A, Mix(surface.R, ink.R), Mix(surface.G, ink.G), Mix(surface.B, ink.B)));
            brush.Freeze(); return brush;
        }
    }
}

internal static class SpatialWheel
{
    internal static bool HoldMoved(double dx, double dy) => dx * dx + dy * dy >= PointerPress.TapDistance * PointerPress.TapDistance;
    internal static int Hit(AppearanceLayout mode, double dx, double dy)
    {
        var point = new Point(210 + dx, 210 + dy);
        for (int i = 0; i < 8; i++)
        {
            var p = LabelPosition(i);
            if (mode == AppearanceLayout.Typefield && new Rect(p.X - 44, p.Y - 30, 88, 68).Contains(point)) return i;
        }
        return -1;
    }
    private static Point Polar(double radius, double degrees)
    { double a = degrees * Math.PI / 180; return new(210 + radius * Math.Cos(a), 210 + radius * Math.Sin(a)); }
    internal static Point LabelPosition(int index) => Polar(130, index * 45 - 90);
}

internal static class SwitcherPlacement
{
    internal static Rect Place(AppearanceLayout mode, double x, double y, double width, double height, Rect area, double dpi = 1)
    {
        if (mode == AppearanceLayout.Classic) return RailPlacement.Place(x, y, width, height, area, 18 * dpi);
        width = Math.Min(width, area.Width); height = Math.Min(height, area.Height);
        double scale = Math.Min(width / 254, height / 376);
        double left = x + 18 * dpi;
        if (mode == AppearanceLayout.Typefield && left + width > area.Right) left = x - 18 * dpi - width;
        double top = y - 38 * scale;
        return new(Math.Clamp(left, area.Left, area.Right - width), Math.Clamp(top, area.Top, area.Bottom - height), width, height);
    }
}
