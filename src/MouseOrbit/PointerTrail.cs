using System.Windows.Media;

namespace MouseOrbit;

internal sealed class PointerPath
{
    private readonly List<Point> _points = new();
    public IReadOnlyList<Point> Points => _points;
    public int Version { get; private set; }
    public Rect Bounds { get; private set; } = Rect.Empty;
    public void Begin(Point point) { _points.Clear(); _points.Add(point); Bounds = new Rect(point, point); Version++; }
    public void Add(Point point)
    {
        if (_points.Count == 0) return;
        if ((point - _points[^1]).LengthSquared < 4) return;
        if (_points.Count >= 2048)
        {
            var reduced = _points.Where((_, i) => i % 2 == 0).ToArray(); _points.Clear(); _points.AddRange(reduced);
        }
        _points.Add(point); var bounds = Bounds; bounds.Union(point); Bounds = bounds; Version++;
    }
    public void Clear() { _points.Clear(); Bounds = Rect.Empty; Version++; }
}

internal sealed class PointerTrail : IDisposable
{
    public PointerPath Path { get; } = new();
    private readonly Dictionary<string, LayeredSurface> _windows = new();
    private int _rendered = -1;
    public void Prepare()
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            Overlay(screen.DeviceName).Paint(new Rect(screen.Bounds.Left, screen.Bounds.Top, 128, 128), graphics =>
            {
                using var pen = new System.Drawing.Pen(System.Drawing.Color.Gray, 2.5f);
                graphics.DrawLine(pen, 16, 16, 80, 80);
            }, false);
    }
    private LayeredSurface Overlay(string name)
    {
        if (_windows.TryGetValue(name, out var existing)) return existing;
        var overlay = new LayeredSurface("MouseOrbit · 轨迹"); _windows.Add(name, overlay); return overlay;
    }
    internal static Rect SurfaceBounds(Rect stroke, System.Drawing.Rectangle screen)
    {
        stroke.Inflate(16, 16); stroke.Intersect(new Rect(screen.X, screen.Y, screen.Width, screen.Height));
        if (stroke.IsEmpty) return Rect.Empty;
        const int block = 128;
        double x = screen.Left + Math.Floor((stroke.Left - screen.Left) / block) * block;
        double y = screen.Top + Math.Floor((stroke.Top - screen.Top) / block) * block;
        double right = Math.Min(screen.Right, screen.Left + Math.Ceiling((stroke.Right - screen.Left) / block) * block);
        double bottom = Math.Min(screen.Bottom, screen.Top + Math.Ceiling((stroke.Bottom - screen.Top) / block) * block);
        return new Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }
    public void Refresh(Color? inkColor = null)
    {
        if (Path.Version == _rendered || Path.Points.Count < 2) return;
        _rendered = Path.Version;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = SurfaceBounds(Path.Bounds, screen.Bounds);
            if (bounds.IsEmpty)
            {
                if (_windows.TryGetValue(screen.DeviceName, out var old)) old.Hide();
                continue;
            }
            var overlay = Overlay(screen.DeviceName);
            overlay.Move((int)bounds.X, (int)bounds.Y);
            double scale = Math.Max(1, Native.GetDpiForWindow(overlay.Handle) / 96d);
            var points = Path.Points.Select(p => new System.Drawing.PointF((float)(p.X - bounds.X), (float)(p.Y - bounds.Y))).ToArray();
            var accent = inkColor ?? Palette.Accent.Color;
            overlay.Paint(bounds, graphics =>
            {
                using var halo = new System.Drawing.Pen(System.Drawing.Color.FromArgb(65, 20, 20, 20), (float)(5 * scale));
                using var ink = new System.Drawing.Pen(System.Drawing.Color.FromArgb(accent.A, accent.R, accent.G, accent.B), (float)(2.5 * scale));
                halo.StartCap = halo.EndCap = ink.StartCap = ink.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                halo.LineJoin = ink.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                graphics.DrawLines(halo, points); graphics.DrawLines(ink, points);
            });
        }
    }
    public void Clear() { Path.Clear(); foreach (var overlay in _windows.Values) overlay.Hide(); }
    public void Dispose() { foreach (var overlay in _windows.Values) overlay.Dispose(); _windows.Clear(); }
}

internal sealed class PointerTrailVisual : FrameworkElement
{
    public IReadOnlyList<Point> Points { get; set; } = [];
    public Point Origin { get; set; }
    public double Scale { get; set; } = 1;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (Points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            Point Local(Point p) => new((p.X - Origin.X) / Scale, (p.Y - Origin.Y) / Scale);
            context.BeginFigure(Local(Points[0]), false, false);
            context.PolyLineTo(Points.Skip(1).Select(Local).ToArray(), true, false);
        }
        geometry.Freeze();
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        var halo = new Pen(new SolidColorBrush(Color.FromArgb(65, 20, 20, 20)), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var ink = new Pen(Palette.Accent, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, halo, geometry); dc.DrawGeometry(null, ink, geometry); dc.Pop();
    }
}
