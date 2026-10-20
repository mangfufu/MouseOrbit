using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MouseOrbit;

internal sealed class CaptureTools : IDisposable
{
    public bool Busy { get; private set; }
    public event Action<string>? Error;
    private readonly List<CaptureOverlay> _overlays = new();
    private GifRecorderWindow? _recorder;
    private GifPreviewWindow? _preview;
    private TaskCompletionSource? _completion;
    private bool _closingOverlays;
    public async Task Start()
    {
        if (Busy) return; Busy = true; _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var frames = await Task.Run(() => screens.Select(s => (s.Bounds, Bitmap: ScreenPixels.Read(s.Bounds))).ToArray());
            foreach (var frame in frames)
            {
                var overlay = new CaptureOverlay(frame.Bitmap, frame.Bounds);
                overlay.Chosen += () => { foreach (var other in _overlays) if (other != overlay) other.Hide(); };
                overlay.Finished += End;
                overlay.Closed += (_, _) => { if (!_closingOverlays && Busy) End(); };
                overlay.GifRegion += region =>
                {
                    CloseOverlays();
                    _recorder = new GifRecorderWindow(region);
                    _recorder.Canceled += End;
                    _recorder.Failed += text => { Error?.Invoke(text); End(); };
                    _recorder.Completed += path =>
                    {
                        _recorder = null;
                        try
                        {
                            _preview = new GifPreviewWindow(path, region);
                            _preview.Done += End;
                            _preview.Again += () => { End(); _ = Start(); };
                            _preview.Open();
                        }
                        catch (Exception ex) { Error?.Invoke("GIF 预览失败：" + ex.Message); try { File.Delete(path); } catch (IOException) { } End(); }
                    };
                    _recorder.Open();
                };
                _overlays.Add(overlay);
            }
            foreach (var overlay in _overlays) overlay.Open();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ExternalException or ArgumentException)
        { Error?.Invoke("截图失败：" + ex.Message); End(); }
        if (_completion != null) await _completion.Task;
    }
    private void CloseOverlays()
    {
        _closingOverlays = true;
        try { foreach (var window in _overlays.ToArray()) window.Close(); _overlays.Clear(); }
        finally { _closingOverlays = false; }
    }
    private void End()
    {
        if (!Busy) return; Busy = false; CloseOverlays(); _recorder = null; _preview = null;
        _completion?.TrySetResult();
    }
    public void Dispose() { _recorder?.Cancel(); _preview?.Close(); End(); }
}

internal sealed class CaptureOverlay : Window
{
    private readonly BitmapSource _screen;
    private readonly System.Drawing.Rectangle _area;
    private readonly CaptureSurface _surface;
    private readonly WrapPanel _toolbar = new();
    private readonly Dictionary<string, Button> _tools = new();
    private readonly TextBlock _message = new() { Foreground = Palette.Accent, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private double _scale = 1;
    internal CaptureSurface Surface => _surface;
    public event Action? Chosen;
    public event Action? Finished;
    public event Action<System.Drawing.Rectangle>? GifRegion;
    public CaptureOverlay(BitmapSource screen, System.Drawing.Rectangle area)
    {
        _screen = screen; _area = area;
        Width = area.Width; Height = area.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        AllowsTransparency = true; Background = Brushes.Transparent; Cursor = Cursors.Cross;
        _surface = new CaptureSurface(screen); Content = _surface;
        InitialCancel();
        _surface.RegionSelected += Selection;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MouseOrbit;component/Controls.xaml", UriKind.Relative) }); Palette.Resources(Resources);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd, -20, Native.GetWindowLong(hwnd, -20) | 0x80);
        };
    }
    public void Open()
    {
        Native.Position(this, _area.Left, _area.Top); _scale = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96d;
        Width = _area.Width / _scale; Height = _area.Height / _scale;
        _surface.Scale = _scale; Native.Position(this, _area.Left, _area.Top); Show();
        InitialCancel();
    }
    private void InitialCancel()
    {
        _surface.Children.Clear();
        var cancel = Tool("取消", () => Finished?.Invoke());
        cancel.Background = Palette.Paper;
        _surface.Children.Add(cancel);
        Canvas.SetLeft(cancel, Math.Max(8, Width - 80)); Canvas.SetTop(cancel, 16);
    }
    private Button Tool(string label, Action action)
    {
        var button = Palette.Button(label, action, label); button.Padding = new Thickness(9, 8, 9, 8); button.Margin = new Thickness(1); button.FontSize = 12; return button;
    }
    private void Selection()
    {
        Chosen?.Invoke();
        _toolbar.Children.Clear(); _tools.Clear();
        foreach (var (id, label) in new[] { ("rectangle", "矩形"), ("ellipse", "圆形"), ("arrow", "箭头"), ("pen", "画笔"), ("text", "文字") })
        {
            var button = Tool(label, () => SelectTool(id)); _tools[id] = button; _toolbar.Children.Add(button);
        }
        foreach (var hex in new[] { "#E75B3D", "#E8B63A", "#39AE77", "#428BDC", "#222222", "#FFFFFF" })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var button = Palette.Button(new Border { Width = 15, Height = 15, Background = new SolidColorBrush(color), BorderBrush = Palette.Muted, BorderThickness = new Thickness(.5) }, () => _surface.Ink = color, "标注颜色");
            button.Padding = new Thickness(4); button.VerticalAlignment = VerticalAlignment.Center; _toolbar.Children.Add(button);
        }
        var width = new ComboBox { ItemsSource = new double[] { 2, 3, 5, 8, 12 }, SelectedIndex = 1, Width = 48, Margin = new Thickness(4), ToolTip = "线条粗细" };
        width.SelectionChanged += (_, _) => _surface.LineWidth = (double)width.SelectedItem; _toolbar.Children.Add(width);
        var font = new ComboBox { ItemsSource = new double[] { 12, 16, 20, 24, 32, 40, 56 }, SelectedIndex = 3, Width = 64, Margin = new Thickness(4), ToolTip = "文字大小" };
        font.SelectionChanged += (_, _) => _surface.FontSize = (double)font.SelectedItem; _toolbar.Children.Add(font);
        _toolbar.Children.Add(Tool("贴字", () => { try { SelectTool("text"); _surface.InsertClipboardText(); } catch (ExternalException) { _message.Text = "剪贴板暂不可用"; } }));
        _toolbar.Children.Add(Tool("撤销", () => { _surface.CommitText(); _surface.Document?.Undo(); _surface.InvalidateVisual(); }));
        _toolbar.Children.Add(Tool("重做", () => { _surface.Document?.Redo(); _surface.InvalidateVisual(); }));
        _toolbar.Children.Add(Tool("重选", () => { _surface.Reset(); InitialCancel(); }));
        _toolbar.Children.Add(Tool("复制", () => Export(false)));
        _toolbar.Children.Add(Tool("保存", () => Export(true)));
        _toolbar.Children.Add(Tool("GIF", StartGif));
        _toolbar.Children.Add(Tool("×", () => Finished?.Invoke()));
        _toolbar.Children.Add(_message);
        var border = new Border { Background = Palette.Paper, BorderBrush = Palette.Edge, BorderThickness = new Thickness(1), Padding = new Thickness(3), Child = _toolbar, Cursor = Cursors.Arrow };
        _surface.Children.Clear(); _surface.Children.Add(border);
        double max = Math.Min(970, Width - 12); _toolbar.MaxWidth = Math.Max(150, max - 8);
        border.Measure(new Size(max, double.PositiveInfinity));
        var rect = _surface.Region;
        Canvas.SetLeft(border, Math.Clamp(rect.Right - border.DesiredSize.Width, 5, Math.Max(5, Width - border.DesiredSize.Width - 5)));
        double y = rect.Bottom + 9;
        if (y + border.DesiredSize.Height > Height - 5) y = rect.Top - border.DesiredSize.Height - 9;
        Canvas.SetTop(border, Math.Clamp(y, 5, Math.Max(5, Height - border.DesiredSize.Height - 5)));
        SelectTool(_surface.Tool);
    }
    private void StartGif()
    {
        if (_surface.Document == null) return;
        _surface.CommitText();
        var r = _surface.Region; double scale = _surface.Scale;
        GifRegion?.Invoke(new(_area.Left + (int)Math.Round(r.X * scale), _area.Top + (int)Math.Round(r.Y * scale), _surface.Document.Image.PixelWidth, _surface.Document.Image.PixelHeight));
    }
    private void SelectTool(string id)
    {
        _surface.CommitText(); _surface.Tool = id;
        foreach (var pair in _tools) pair.Value.Background = pair.Key == id ? Palette.Selected : Brushes.Transparent;
    }
    private void Export(bool save)
    {
        _surface.CommitText(); if (_surface.Document == null) return;
        try
        {
            var image = _surface.Document.Render();
            if (save)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = "MouseOrbit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), DefaultExt = ".png" };
                if (dialog.ShowDialog(this) != true) return; ScreenPixels.SavePng(image, dialog.FileName);
            }
            else Clipboard.SetImage(image);
            Finished?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or InvalidOperationException) { _message.Text = "操作失败，请重试"; }
    }
}

internal sealed class CaptureSurface : Canvas
{
    private BitmapSource _screen;
    private Point _start;
    private bool _drawing;
    private Markup? _current;
    private readonly List<Point> _pen = new();
    private TextBox? _text;
    private Point _textPoint;
    public double Scale { get; set; } = 1;
    public Rect Region { get; private set; }
    public CaptureDocument? Document { get; private set; }
    public string Tool { get; set; } = "rectangle";
    public Color Ink { get; set; } = Color.FromRgb(231, 91, 61);
    public double LineWidth { get; set; } = 3;
    public double FontSize { get; set; } = 24;
    public event Action? RegionSelected;
    public CaptureSurface(BitmapSource screen)
    {
        _screen = screen; Background = Brushes.Transparent;
        MouseLeftButtonDown += Down; MouseMove += Move; MouseLeftButtonUp += Up;
    }
    private bool IsControl(DependencyObject? original)
    {
        for (var p = original; p != null && p != this; p = p is Visual ? VisualTreeHelper.GetParent(p) : null)
            if (p is Button or ComboBox or TextBox) return true;
        return false;
    }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, ActualWidth), Math.Clamp(p.Y, 0, ActualHeight));
    private Point Local(Point p) => new(Math.Clamp((p.X - Region.X) * Scale, 0, Document!.Image.PixelWidth), Math.Clamp((p.Y - Region.Y) * Scale, 0, Document!.Image.PixelHeight));
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (IsControl(e.OriginalSource as DependencyObject)) return;
        CommitText(); _start = Clamp(e.GetPosition(this));
        if (Document != null && !Region.Contains(_start)) return;
        e.Handled = true;
        if (Document != null && Tool == "text") { BeginText(_start); return; }
        _drawing = true; _pen.Clear(); if (Document != null) _pen.Add(Local(_start)); CaptureMouse();
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        var point = Clamp(e.GetPosition(this));
        if (Document == null) Region = new Rect(_start, point);
        else
        {
            var local = Local(point); if (Tool == "pen" && _pen.Count < 4096) _pen.Add(local);
            _current = new(Tool, Local(_start), local, Ink, LineWidth * Scale, FontSize * Scale, Points: _pen.ToArray());
        }
        InvalidateVisual();
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return; Move(sender, e); _drawing = false; ReleaseMouseCapture(); e.Handled = true;
        if (Document == null)
        {
            SelectRegion(Region);
        }
        else if (_current != null) { Document.Add(_current); _current = null; }
        InvalidateVisual();
    }
    internal bool SelectRegion(Rect region)
    {
        int x = Math.Clamp((int)Math.Round(region.X * Scale), 0, _screen.PixelWidth - 1), y = Math.Clamp((int)Math.Round(region.Y * Scale), 0, _screen.PixelHeight - 1);
        int w = Math.Min((int)Math.Round(region.Width * Scale), _screen.PixelWidth - x), h = Math.Min((int)Math.Round(region.Height * Scale), _screen.PixelHeight - y);
        if (w < 3 || h < 3) { Region = Rect.Empty; InvalidateVisual(); return false; }
        Region = new Rect(x / Scale, y / Scale, w / Scale, h / Scale);
        var crop = new CroppedBitmap(_screen, new Int32Rect(x, y, w, h)); crop.Freeze(); Document = new(crop); RegionSelected?.Invoke(); InvalidateVisual(); return true;
    }
    private void BeginText(Point point)
    {
        _textPoint = Local(point);
        _text = new TextBox { FontSize = FontSize, Foreground = new SolidColorBrush(Ink), Background = Palette.B("#DFFFFFFF"), BorderBrush = Palette.Accent, MinWidth = 100, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(100, Region.Right - point.X), Padding = new Thickness(3) };
        SetLeft(_text, point.X); SetTop(_text, point.Y); Children.Add(_text); _text.Focus();
    }
    public void InsertClipboardText()
    {
        if (Document == null || !Clipboard.ContainsText()) return;
        if (_text == null) BeginText(new Point(Region.Left + 8, Region.Top + 8));
        _text!.Text = Clipboard.GetText();
    }
    public void CommitText()
    {
        if (_text == null) return;
        if (!string.IsNullOrWhiteSpace(_text.Text)) Document?.Add(new("text", _textPoint, _textPoint, Ink, LineWidth * Scale, FontSize * Scale, _text.Text));
        Children.Remove(_text); _text = null; InvalidateVisual();
    }
    public void Reset() { CommitText(); Document = null; Region = Rect.Empty; _current = null; InvalidateVisual(); }
    internal void ShowFrame(BitmapSource frame)
    {
        if (frame.PixelWidth != _screen.PixelWidth || frame.PixelHeight != _screen.PixelHeight) throw new ArgumentException("Frame dimensions changed");
        _screen = frame; Document?.SetImage(frame); InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var all = new Rect(0, 0, ActualWidth, ActualHeight); dc.DrawImage(_screen, all);
        Geometry shade = new RectangleGeometry(all);
        if (!Region.IsEmpty && Region.Width > 0 && Region.Height > 0) shade = Geometry.Combine(shade, new RectangleGeometry(Region), GeometryCombineMode.Exclude, null);
        dc.DrawGeometry(Palette.B("#70000000"), null, shade);
        if (!Region.IsEmpty && Region.Width > 0 && Region.Height > 0)
        {
            dc.DrawRectangle(null, new Pen(Palette.Accent, 1.5), Region);
            if (Document != null)
            {
                dc.PushClip(new RectangleGeometry(Region)); dc.PushTransform(new TranslateTransform(Region.X, Region.Y)); dc.PushTransform(new ScaleTransform(1 / Scale, 1 / Scale));
                Document.DrawMarks(dc, _current); dc.Pop(); dc.Pop(); dc.Pop();
            }
        }
    }
}
