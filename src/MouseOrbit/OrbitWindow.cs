using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Animation;

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
        Content = Visual;
        SourceInitialized += (_, _) => Native.OverlayStyle(this, true);
    }
    public void Open(Native.Point point, Preferences preferences, bool gesture = false, int page = 0, bool holdToSelect = false)
    {
        HoldToSelect = holdToSelect;
        Visual.Settings = preferences; Visual.Page = page; Visual.GestureMode = gesture; Visual.Pattern = ""; Visual.Selected = -1; Visual.Trail.Clear();
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
        Visual.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(120)));
        Visual.InvalidateVisual();
    }
    public void Update(Native.Point point, string? pattern = null)
    {
        var dx = (point.X - CenterPixels.X) / Scale;
        var dy = (point.Y - CenterPixels.Y) / Scale;
        int selected = !HoldToSelect && dx * dx + dy * dy > 205 * 205 ? -1 : WheelMath.Sector(dx, dy, 55);
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
    public OrbitVisual()
    {
        Loaded += (_, _) => Palette.Changed += InvalidateVisual;
        Unloaded += (_, _) => Palette.Changed -= InvalidateVisual;
    }
    public Preferences Settings { get; set; } = new();
    public int Selected { get; set; } = -1;
    public int Page { get; set; }
    public bool GestureMode { get; set; }
    public string Pattern { get; set; } = "";
    public List<System.Windows.Point> Trail { get; } = new();
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
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
        for (int i = 0; i < 8; i++)
        {
            double angle = (i * 45 - 90) * Math.PI / 180;
            var p = new System.Windows.Point(210 + 138 * Math.Cos(angle), 210 + 138 * Math.Sin(angle));
            bool active = i == Selected;
            var rect = new Rect(p.X - 38, p.Y - 38, 76, 76);
            double radius = Palette.Pixel ? 1 : Palette.Current.Radius;
            dc.DrawRoundedRectangle(Palette.B("#0A435158"), null, new Rect(rect.X, rect.Y + 2, rect.Width, rect.Height), radius, radius);
            dc.DrawRoundedRectangle(active ? Palette.Selected : Palette.Surface, new Pen(Palette.Edge, 1), rect, radius, radius);
            var command = Commands.Find(Settings.Wheel[i]);
            dc.DrawImage(CommandIcons.Get(command.Id), new Rect(p.X - 12, p.Y - 22, 24, 24));
            Text(dc, command.Name, 10.5, Palette.Ink, p.X, p.Y + 20, active);
        }
        dc.Pop();
    }
}
