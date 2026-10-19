using System.Windows.Controls;
using System.Windows.Media;

namespace MouseOrbit;

internal sealed class RecordingOutline : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly Border _stroke = new() { BorderBrush = Palette.Muted, BorderThickness = new Thickness(2), Background = Brushes.Transparent };
    public RecordingOutline(System.Drawing.Rectangle region)
    {
        _region = region; Title = "MouseOrbit · 录制范围";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Content = _stroke;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; IsHitTestVisible = false;
        SourceInitialized += (_, _) => Native.OverlayStyle(this, true);
    }
    public void Open()
    {
        Native.Position(this, _region.Left, _region.Top);
        double dpi = Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle) / 96d;
        _stroke.BorderThickness = new Thickness(2 / dpi);
        Width = _region.Width / dpi; Height = _region.Height / dpi; Show();
        if (!Native.ExcludeFromCapture(this))
        {
            // Older systems: draw outside the captured pixels instead.
            Native.Position(this, _region.Left - 2, _region.Top - 2);
            Width = (_region.Width + 4) / dpi; Height = (_region.Height + 4) / dpi;
        }
    }
    public void Recording(bool running) => _stroke.BorderBrush = running ? new SolidColorBrush(Color.FromRgb(197, 100, 82)) : Palette.Muted;
}
