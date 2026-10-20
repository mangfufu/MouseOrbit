using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MouseOrbit;

internal sealed class GifPreviewWindow : Window
{
    private readonly string _path;
    private readonly System.Drawing.Rectangle _region;
    private readonly GifAnimation _animation;
    private readonly CaptureSurface _surface;
    private readonly Border _shell;
    private readonly WrapPanel _tools = new() { Margin = new Thickness(8, 0, 8, 4), Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, Button> _toolButtons = new();
    private readonly TextBlock _status = new() { Foreground = Palette.Muted, FontSize = 11, Margin = new Thickness(12, 4, 12, 4) };
    private readonly DispatcherTimer _timer = new();
    private readonly Button _play, _edit;
    private int _index;
    private bool _editing, _saving;
    public event Action? Done;
    public event Action? Again;
    internal CaptureSurface Surface => _surface;
    public GifPreviewWindow(string path, System.Drawing.Rectangle region)
    {
        _path = path; _region = region; Title = "GIF 预览与标注";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; AllowsTransparency = true;
        Background = Brushes.Transparent; Foreground = Palette.Ink; ShowInTaskbar = false; Topmost = true;
        MinWidth = 360; MinHeight = 240;
        _animation = new(path);
        var first = _animation.Frame(0);
        _surface = new CaptureSurface(first) { Width = first.PixelWidth, Height = first.PixelHeight, IsHitTestVisible = false };
        _surface.SelectRegion(new Rect(0, 0, first.PixelWidth, first.PixelHeight));
        var layout = new DockPanel();
        _shell = new Border { Background = Palette.Paper, BorderBrush = Palette.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = layout, Margin = new Thickness(3) };
        Content = _shell;
        var header = new DockPanel { Margin = new Thickness(10, 4, 6, 4) };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var close = Palette.Button("×", Close, "关闭预览"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { Text = "GIF · 录制完成", Foreground = Palette.Ink, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        title.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        header.Children.Add(title);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        bottom.Children.Add(_tools); BuildTools(); bottom.Children.Add(_status);
        var bar = new WrapPanel { Margin = new Thickness(8, 0, 8, 8) }; bottom.Children.Add(bar);
        var save = Palette.Button("保存 GIF", () => _ = Save(), "保存标注后的 GIF"); save.Background = Palette.Selected; bar.Children.Add(save);
        _play = Palette.Button("暂停", TogglePlayback, "播放或暂停预览"); bar.Children.Add(_play);
        _edit = Palette.Button("标注", () => SetEditing(!_editing), "暂停并标注整段 GIF"); bar.Children.Add(_edit);
        bar.Children.Add(Palette.Button("重录", () => { if (_saving) return; Close(); Again?.Invoke(); }, "重新录制"));
        layout.Children.Add(new Border { Background = Palette.Surface, Margin = new Thickness(10, 0, 10, 0), ClipToBounds = true, Child = new Viewbox { Stretch = Stretch.Uniform, Child = _surface } });
        _timer.Tick += (_, _) => { _index = (_index + 1) % _animation.Count; ShowFrame(); };
        ShowFrame(); _timer.Start();
        Closing += (_, e) => { if (_saving) { e.Cancel = true; _status.Text = "正在保存，请稍候…"; } };
        Closed += (_, _) =>
        {
            _timer.Stop(); _animation.Dispose();
            try { File.Delete(_path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Done?.Invoke();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !_saving) { Close(); e.Handled = true; } };
    }
    public void Open()
    {
        var area = System.Windows.Forms.Screen.FromRectangle(_region).WorkingArea;
        Native.Position(this, _region.Left, _region.Top);
        double dpi = Math.Max(1, Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle) / 96d);
        double availableWidth = area.Width / dpi - 20, availableHeight = area.Height / dpi - 20;
        MinWidth = Math.Min(MinWidth, availableWidth); MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(availableWidth, Math.Clamp(_surface.Width / dpi + 28, 440, 640));
        double pictureHeight = (Width - 28) * _surface.Height / _surface.Width;
        Height = Math.Min(availableHeight, Math.Clamp(pictureHeight + 122, 260, 460));
        int x = Math.Clamp(_region.Left, area.Left + 4, Math.Max(area.Left + 4, area.Right - (int)(Width * dpi) - 4));
        int y = Math.Clamp(_region.Top, area.Top + 4, Math.Max(area.Top + 4, area.Bottom - (int)(Height * dpi) - 4));
        Native.Position(this, x, y); Show();
        var slide = new TranslateTransform(0, 8); _shell.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
    private void BuildTools()
    {
        foreach (var (id, label) in new[] { ("rectangle", "矩形"), ("ellipse", "圆形"), ("arrow", "箭头"), ("pen", "画笔"), ("text", "文字") })
        {
            var button = Palette.Button(label, () => SelectTool(id), label); button.Padding = new Thickness(7, 5, 7, 5);
            _toolButtons[id] = button; _tools.Children.Add(button);
        }
        foreach (var color in new[] { Colors.OrangeRed, Colors.Gold, Colors.LimeGreen, Colors.DodgerBlue, Colors.White, Colors.Black })
        {
            var button = Palette.Button(new Border { Width = 12, Height = 12, Background = new SolidColorBrush(color), BorderBrush = Palette.Muted, BorderThickness = new Thickness(.5) }, () => _surface.Ink = color, "标注颜色");
            button.Padding = new Thickness(5); _tools.Children.Add(button);
        }
        var width = new ComboBox { ItemsSource = new double[] { 2, 3, 5, 8, 12 }, SelectedIndex = 1, Width = 46, Margin = new Thickness(3), ToolTip = "线条粗细" };
        width.SelectionChanged += (_, _) => _surface.LineWidth = (double)width.SelectedItem; _tools.Children.Add(width);
        var font = new ComboBox { ItemsSource = new double[] { 16, 24, 32, 40, 56 }, SelectedIndex = 1, Width = 48, Margin = new Thickness(3), ToolTip = "文字大小" };
        font.SelectionChanged += (_, _) => _surface.FontSize = (double)font.SelectedItem; _tools.Children.Add(font);
        _tools.Children.Add(Palette.Button("撤销", () => { _surface.CommitText(); _surface.Document?.Undo(); _surface.InvalidateVisual(); }, "撤销标注"));
        _tools.Children.Add(Palette.Button("重做", () => { _surface.Document?.Redo(); _surface.InvalidateVisual(); }, "重做标注"));
    }
    private void SelectTool(string id)
    {
        _surface.CommitText(); _surface.Tool = id;
        foreach (var tool in _toolButtons) tool.Value.Background = tool.Key == id ? Palette.Selected : Brushes.Transparent;
    }
    private void SetEditing(bool editing)
    {
        if (_saving) return;
        _surface.CommitText(); _editing = editing;
        _tools.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _surface.IsHitTestVisible = editing; _surface.Cursor = editing ? Cursors.Cross : Cursors.Arrow;
        _edit.Background = editing ? Palette.Selected : Brushes.Transparent;
        if (editing) { _timer.Stop(); _play.Content = "播放"; SelectTool(_surface.Tool); }
        _status.Text = editing ? "标注应用于整段 GIF · 点击画面绘制" : $"{_surface.Width:0} × {_surface.Height:0} · {_animation.Count} 帧";
    }
    private void TogglePlayback()
    {
        if (_saving) return;
        if (_timer.IsEnabled) { _timer.Stop(); _play.Content = "播放"; }
        else { SetEditing(false); _timer.Start(); _play.Content = "暂停"; }
    }
    private void ShowFrame()
    {
        _surface.ShowFrame(_animation.Frame(_index));
        _timer.Interval = TimeSpan.FromMilliseconds(_animation.Delay(_index));
        if (!_editing) _status.Text = $"{_surface.Width:0} × {_surface.Height:0} · {_animation.Count} 帧";
    }
    private async Task Save()
    {
        if (_saving) return;
        _surface.CommitText(); _timer.Stop(); _play.Content = "播放";
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "GIF 动图|*.gif", FileName = "MouseOrbit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), DefaultExt = ".gif" };
        if (dialog.ShowDialog(this) != true) return;
        _saving = true; _shell.IsEnabled = false;
        try
        {
            var marks = _surface.Document!.Marks.ToArray();
            await _animation.Export(dialog.FileName, marks, (done, total) => _status.Text = $"保存中 {done}/{total}");
            _saving = false; Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        { _status.Text = "保存失败：" + ex.Message; }
        finally { _saving = false; _shell.IsEnabled = true; }
    }
}
