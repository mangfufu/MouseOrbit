using System.Windows.Media;

namespace MouseOrbit;

internal sealed class PointerPath
{
    private readonly List<Point> _points = new();
    public IReadOnlyList<Point> Points => _points;
    public int Version { get; private set; }
    public void Begin(Point point) { _points.Clear(); _points.Add(point); Version++; }
    public void Add(Point point)
    {
        if (_points.Count == 0) return;
        if ((point - _points[^1]).LengthSquared < 4) return;
        if (_points.Count >= 2048)
        {
            var reduced = _points.Where((_, i) => i % 2 == 0).ToArray(); _points.Clear(); _points.AddRange(reduced);
        }
        _points.Add(point); Version++;
    }
    public void Clear() { _points.Clear(); Version++; }
}

internal sealed class PointerTrail : IDisposable
{
    public PointerPath Path { get; } = new();
    private readonly Dictionary<string, (Window Window, PointerTrailVisual View)> _windows = new();
    private int _rendered = -1;
    public void Refresh()
    {
        if (Path.Version == _rendered || Path.Points.Count < 2) return;
        _rendered = Path.Version;
        var points = Path.Points.ToArray();
        var extent = new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)), new Point(points.Max(p => p.X), points.Max(p => p.Y)));
        extent.Inflate(5, 5);
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            if (!extent.IntersectsWith(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height))) continue;
            if (!_windows.TryGetValue(screen.DeviceName, out var overlay))
            {
                var view = new PointerTrailVisual();
                var window = new Window
                {
                    Title = "MouseOrbit · 轨迹", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    AllowsTransparency = true, Background = Brushes.Transparent, Content = view,
                    ShowActivated = false, ShowInTaskbar = false, Topmost = true, IsHitTestVisible = false
                };
                window.SourceInitialized += (_, _) => Native.OverlayStyle(window, true);
                overlay = (window, view); _windows.Add(screen.DeviceName, overlay);
            }
            Native.Position(overlay.Window, bounds.Left, bounds.Top);
            double scale = Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(overlay.Window).Handle) / 96d;
            overlay.Window.Width = bounds.Width / scale; overlay.Window.Height = bounds.Height / scale;
            overlay.View.Origin = new Point(bounds.Left, bounds.Top); overlay.View.Scale = scale;
            overlay.View.Points = points; overlay.View.InvalidateVisual();
            if (!overlay.Window.IsVisible) overlay.Window.Show();
        }
    }
    public void Clear() { Path.Clear(); foreach (var overlay in _windows.Values) overlay.Window.Hide(); }
    public void Dispose() { foreach (var overlay in _windows.Values) overlay.Window.Close(); _windows.Clear(); }
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
