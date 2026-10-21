using System.Globalization;
using System.Windows.Media;

namespace MouseOrbit;

internal sealed class OrbitWindow : Window
{
    public OrbitVisual Visual { get; } = new();
    public System.Windows.Point CenterPixels { get; private set; }
    public double Scale { get; private set; } = 1;
    public bool HoldToSelect { get; private set; }
    public OrbitWindow()
    {
        Title = "MouseOrbit · 轮盘"; Width = Height = 420;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Ui.Install(this); Content = Visual;
        SourceInitialized += (_, _) => Native.OverlayStyle(this, true);
    }
    public void Open(Native.Point point, Preferences preferences, bool gesture = false, int page = 0, bool holdToSelect = false, bool targetPinned = false)
    {
        if (!IsVisible) BackdropTone.Adapt(point);
        HoldToSelect = holdToSelect;
        Visual.Settings = preferences; Visual.Page = page; Visual.GestureMode = gesture; Visual.Pattern = ""; Visual.Selected = -1; Visual.Trail.Clear();
        Visual.TargetPinned = targetPinned;
        Native.Position(this, point.X, point.Y);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        double dpi = Native.GetDpiForWindow(handle) / 96d;
        var bounds = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        Width = Height = Math.Min(420, Math.Min(bounds.Width, bounds.Height) / dpi);
        Scale = dpi * Width / 420;
        int half = (int)(210 * Scale);
        int x = Math.Clamp(point.X - half, bounds.Left, Math.Max(bounds.Left, bounds.Right - half * 2));
        int y = Math.Clamp(point.Y - half, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - half * 2));
        Native.Position(this, x, y);
        CenterPixels = new(x + half, y + half);
        Show();
        Ui.Enter(Visual, 4);
        Visual.InvalidateVisual();
    }
    public void Update(Native.Point point, string? pattern = null)
    {
        var dx = (point.X - CenterPixels.X) / Scale;
        var dy = (point.Y - CenterPixels.Y) / Scale;
        int selected = Palette.Spatial ? SpatialWheel.Hit(Palette.Layout, dx, dy) : !HoldToSelect && dx * dx + dy * dy > 205 * 205 ? -1 : WheelMath.Sector(dx, dy, OrbitVisual.DeadZone);
        if (pattern == null && Visual.Selected == selected) return;
        Visual.Selected = selected;
        if (pattern != null)
        {
            Visual.Pattern = pattern;
            if (Visual.Trail.Count > 250) Visual.Trail.RemoveAt(0);
            Visual.Trail.Add(new(dx + 210, dy + 210));
        }
        Visual.InvalidateVisual();
    }
}

public sealed class OrbitVisual : FrameworkElement
{
    internal const double DeadZone = 96;
    private static readonly Geometry[] Sectors = Enumerable.Range(0, 8).Select(Sector).ToArray();
    private static Point Polar(double radius, double degrees)
    { double angle = degrees * Math.PI / 180; return new(210 + radius * Math.Cos(angle), 210 + radius * Math.Sin(angle)); }
    private static Geometry Sector(int index)
    {
        double center = index * 45 - 90, start = center - 21.2, end = center + 21.2;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(Polar(164, start), true, true);
            path.ArcTo(Polar(164, end), new Size(164, 164), 0, false, SweepDirection.Clockwise, true, false);
            path.LineTo(Polar(98, end), true, false);
            path.ArcTo(Polar(98, start), new Size(98, 98), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze(); return geometry;
    }
    public OrbitVisual()
    {
        Loaded += (_, _) => Palette.Changed += InvalidateVisual;
        Unloaded += (_, _) => Palette.Changed -= InvalidateVisual;
    }
    public Preferences Settings { get; set; } = new();
    public int Selected { get; set; } = -1;
    public int Page { get; set; }
    public bool TargetPinned { get; set; }
    public bool GestureMode { get; set; }
    public string Pattern { get; set; } = "";
    public List<System.Windows.Point> Trail { get; } = new();
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private static SolidColorBrush TileBrush(bool selected)
    {
        var surface = Palette.Surface.Color; var accent = Palette.Accent.Color;
        byte Tint(byte source, byte ink) => (byte)Math.Round(source * .84 + ink * .16);
        var color = selected ? Color.FromArgb(surface.A, Tint(surface.R, accent.R), Tint(surface.G, accent.G), Tint(surface.B, accent.B)) : surface;
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
    private void Text(DrawingContext dc, string value, double size, Brush brush, double x, double y, bool bold = false)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI, Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new(x - text.Width / 2, y - text.Height / 2));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushTransform(new ScaleTransform(ActualWidth / 420, ActualHeight / 420));
        if (GestureMode)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 420, 420)));
            var trailPen = new Pen(Palette.B("#A28DADB7"), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            for (int i = 1; i < Trail.Count; i++) dc.DrawLine(trailPen, Trail[i - 1], Trail[i]);
            dc.DrawRoundedRectangle(Palette.Surface, new Pen(Palette.Edge, 1), new Rect(112, 168, 196, 82), 18, 18);
            Text(dc, GestureRecognizer.Arrows(Pattern), 24, Palette.Ink, 210, 193);
            var label = Settings.GesturesMap.TryGetValue(Pattern, out var id) ? Commands.Find(id).Name : Pattern.Length == 0 ? "" : "未绑定";
            Text(dc, label, 12, Palette.Muted, 210, 225);
            dc.Pop(); dc.Pop(); return;
        }
        if (Palette.Spatial) { SpatialWheel.Draw(dc, this, Text); dc.Pop(); return; }
        // Eight generous sectors share one open ring. Selection preserves alpha and hit targets.
        var idle = TileBrush(false); var hover = TileBrush(true);
        for (int i = 0; i < 8; i++)
        {
            double angle = (i * 45 - 90) * Math.PI / 180;
            var p = new System.Windows.Point(210 + 127 * Math.Cos(angle), 210 + 127 * Math.Sin(angle));
            bool active = i == Selected;
            dc.DrawGeometry(active ? hover : idle, new Pen(Palette.Edge, .8), Sectors[i]);
            dc.PushClip(Sectors[i]);
            // A single shallow lighting change suggests a folded plane without warping its content.
            var sheen = new LinearGradientBrush(Color.FromArgb(Palette.Dark ? (byte)12 : (byte)26, 255, 255, 255), Colors.Transparent, new Point(0, 0), new Point(1, .85));
            dc.DrawRectangle(sheen, null, Sectors[i].Bounds); dc.Pop();
            var edgeColor = active ? Palette.Accent.Color : Palette.Muted.Color;
            var rim = new StreamGeometry();
            using (var path = rim.Open())
            { path.BeginFigure(Polar(164, i * 45 - 96), false, false); path.ArcTo(Polar(164, i * 45 - 84), new Size(164, 164), 0, false, SweepDirection.Clockwise, true, false); }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(100, edgeColor.R, edgeColor.G, edgeColor.B)), 1), rim);
            var command = Commands.Find(Settings.Wheel[i]);
            dc.DrawImage(CommandIcons.Get(command.Id), new Rect(p.X - 11, p.Y - 21, 22, 22));
            bool pinned = command.Id == "topmost" && TargetPinned;
            if (pinned) dc.DrawEllipse(Palette.Accent, null, new Point(p.X + 22, p.Y - 21), 2.5, 2.5);
            Text(dc, pinned ? "取消置顶" : command.Name, 11, Palette.Ink, p.X, p.Y + 18);
        }
        dc.Pop();
    }
}
