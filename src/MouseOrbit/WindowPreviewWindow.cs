using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MouseOrbit;

internal sealed class WindowPreviewWindow : Window
{
    private readonly ScrollViewer _viewport;
    private readonly Grid _image;
    private readonly Image _cached = new() { Stretch = Stretch.Uniform };
    private readonly EntryIcon _icon = new() { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ParticleLabel _title = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 8, 12, 12) };
    private LiveThumbnail? _thumbnail;
    internal WindowPreviewWindow()
    {
        Width = 300; Height = 210; Title = "MouseOrbit · 窗口预览";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
        Ui.Install(this);
        var content = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(_title, Dock.Bottom); content.Children.Add(_title);
        _image = new Grid { Background = Palette.Chrome, MinHeight = 1 }; _image.Children.Add(_icon); _image.Children.Add(_cached);
        _viewport = new ScrollViewer { Content = _image, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        content.Children.Add(_viewport); Content = new ParticlePanel { Child = content };
        SourceInitialized += (_, _) => { Native.OverlayStyle(this, true); Native.DisableTransitions(this); };
        Closed += (_, _) => { _thumbnail?.Dispose(); _thumbnail = null; };
    }
    internal static Rect Place(Rect row, Rect area, double dpi)
    {
        double width = Math.Min(300 * dpi, area.Width), height = Math.Min(210 * dpi, area.Height), gap = 10 * dpi;
        double x = row.Right + gap;
        if (x + width > area.Right) x = row.Left - gap - width;
        return new Rect(Math.Clamp(x, area.Left, area.Right - width), Math.Clamp(row.Top - 8 * dpi, area.Top, area.Bottom - height), width, height);
    }
    internal void Open(SwitchWindow source, FrameworkElement row)
    {
        if (!WindowNative.IsWindow(source.Handle) || WindowNative.GetWindowThreadProcessId(source.Handle, out uint pid) == 0 || pid != source.Process) { Dismiss(); return; }
        _thumbnail?.Dispose(); _thumbnail = null;
        SetSource(source);
        var first = row.PointToScreen(new Point()); var last = row.PointToScreen(new Point(row.ActualWidth, row.ActualHeight));
        var monitor = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)first.X, (int)first.Y));
        var work = monitor.WorkingArea; var handle = new WindowInteropHelper(this).EnsureHandle();
        Native.Position(this, (int)first.X, (int)first.Y);
        double dpi = Math.Max(1, Native.GetDpiForWindow(handle) / 96d);
        var bounds = Place(new Rect(first, last), new Rect(work.X, work.Y, work.Width, work.Height), dpi);
        Width = bounds.Width / dpi; Height = bounds.Height / dpi; Native.Position(this, (int)bounds.X, (int)bounds.Y);
        _thumbnail = new LiveThumbnail(this, _image, _viewport, source, contain: true);
        Show(); UpdateLayout();
    }
    internal void SetSource(SwitchWindow source)
    { _title.Text = source.Title; _icon.Entry = source.Icon; _cached.Source = source.Preview; }
    internal void Dismiss() { _thumbnail?.Dispose(); _thumbnail = null; if (IsVisible) Hide(); }
}
