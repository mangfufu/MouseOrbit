using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MouseOrbit;

internal sealed class ActionToast : Window, IDisposable
{
    private readonly TextBlock _text = new() { FontSize = 13, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    public ActionToast()
    {
        Title = "MouseOrbit · 操作提示"; Width = 174; Height = 40;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent;
        Content = new Border { Background = Palette.Surface, BorderBrush = Palette.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 5, 12, 5), Child = _text };
        SourceInitialized += (_, _) => Native.OverlayStyle(this, true);
        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
    }
    internal void NotifyPinned(bool pinned, Native.Point point)
    {
        _timer.Stop(); _text.Text = pinned ? "● 已置顶" : "○ 已取消置顶";
        Native.Position(this, point.X + 18, point.Y + 20);
        double dpi = Math.Max(1, Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96d);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        Native.Position(this, Math.Clamp(point.X + 18, area.Left, Math.Max(area.Left, area.Right - (int)(Width * dpi))), Math.Clamp(point.Y + 20, area.Top, Math.Max(area.Top, area.Bottom - (int)(Height * dpi))));
        Show(); Native.ExcludeFromCapture(this); _timer.Start();
    }
    public void Dispose() { _timer.Stop(); Close(); }
}
