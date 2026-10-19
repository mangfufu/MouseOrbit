using System.Windows.Controls;
using System.Windows.Media;

namespace MouseOrbit;

internal sealed class GestureHint : Window
{
    private readonly TextBlock _name = new() { FontSize = 14, Foreground = Palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _target = new() { FontSize = 11, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
    private readonly Window _frame;
    public GestureHint()
    {
        Width = 270; Height = 67; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        var stack = new StackPanel(); stack.Children.Add(_name); stack.Children.Add(_target);
        Content = new Border { Background = Palette.Surface, BorderBrush = Palette.Edge, BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 8), Child = stack };
        SourceInitialized += (_, _) => Native.OverlayStyle(this, true);
        _frame = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            Content = new Border { BorderBrush = Palette.Accent, BorderThickness = new Thickness(2), Background = Brushes.Transparent } };
        _frame.SourceInitialized += (_, _) => Native.OverlayStyle(_frame, true);
    }
    public void Update(Native.Point point, WindowTarget target, string name, bool global = false)
    {
        _name.Text = name; _target.Text = global ? "" : OpenWindows.Title(target.Handle);
        Native.Position(this, point.X + 22, point.Y + 20);
        double dpi = Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle) / 96d;
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        Native.Position(this, Math.Clamp(point.X + 22, area.Left, Math.Max(area.Left, area.Right - (int)(Width * dpi))), Math.Clamp(point.Y + 20, area.Top, Math.Max(area.Top, area.Bottom - (int)(Height * dpi))));
        if (!IsVisible) Show();
        if (global || !target.Valid || !WindowNative.GetWindowRect(target.Handle, out var box)) { _frame.Hide(); return; }
        Native.Position(_frame, box.Left, box.Top);
        double scale = Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(_frame).Handle) / 96d;
        _frame.Width = Math.Max(1, box.Width / scale); _frame.Height = Math.Max(1, box.Height / scale);
        if (!_frame.IsVisible) _frame.Show();
    }
    public void Clear() { Hide(); _frame.Hide(); }
    public void Dispose() { _frame.Close(); Close(); }
}
