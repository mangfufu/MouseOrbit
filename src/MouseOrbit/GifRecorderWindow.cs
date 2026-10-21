using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MouseOrbit;

internal sealed class GifRecorderWindow : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly RecordingOutline _outline;
    internal const int FrameMilliseconds = 50;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(FrameMilliseconds) };
    private readonly CheckBox _cursor = new() { Content = "显示鼠标", IsChecked = true, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
    private readonly Stopwatch _watch = new();
    private readonly TextBlock _clock = new() { Text = "GIF 00:00", FontFamily = Palette.BrandFont, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center, Width = 82 };
    private readonly Button _start, _pause, _stop;
    private AnimatedGifWriter? _writer;
    private Task _frame = Task.CompletedTask;
    private string _path = "";
    private bool _running, _paused, _ending;
    private long _previousFrame;
    public event Action? Canceled;
    public event Action<string>? Completed;
    public event Action<string>? Failed;
    public GifRecorderWindow(System.Drawing.Rectangle region)
    {
        _region = region; Width = 472; Height = 64; Ui.Install(this);
        _outline = new RecordingOutline(region);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        var panel = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(_clock);
        panel.Children.Add(_cursor);
        _start = Ui.Action("record", "开始", () => _ = Begin(), "开始录制"); panel.Children.Add(_start);
        _pause = Ui.Action("pause", "暂停", Pause, "暂停或继续"); _pause.IsEnabled = false; panel.Children.Add(_pause);
        _stop = Ui.Action("stop", "结束", () => _ = Stop(), "结束并预览"); _stop.IsEnabled = false; panel.Children.Add(_stop);
        panel.Children.Add(Ui.Icon("close", Cancel, "取消录制"));
        var shell = Ui.Shell(panel, new Thickness(12), 16); shell.Margin = new Thickness(4); Content = shell;
        SourceInitialized += (_, _) => Native.OverlayStyle(this, false);
        Closing += (_, e) => { if (!_ending) { e.Cancel = true; Cancel(); } };
        Closed += (_, _) => _outline.Close();
        _timer.Tick += async (_, _) =>
        {
            _clock.Text = "GIF " + _watch.Elapsed.ToString(@"mm\:ss");
            if (_watch.Elapsed.TotalSeconds >= 60) { await Stop(); return; }
            if (_running && !_paused && _frame.IsCompleted)
            { _frame = CaptureFrame(); await _frame; }
        };
    }
    public void Open()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(_region.Location).WorkingArea;
        Native.Position(this, _region.Left, _region.Bottom + 8);
        var hwnd = new WindowInteropHelper(this).Handle; double scale = Native.GetDpiForWindow(hwnd) / 96d;
        int x = Math.Clamp(_region.Left, screen.Left, Math.Max(screen.Left, screen.Right - (int)(Width * scale)));
        int y = _region.Bottom + 8;
        if (y + Height * scale > screen.Bottom) y = _region.Top - (int)(Height * scale) - 8;
        y = Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - (int)(Height * scale)));
        Native.Position(this, x, y); Show();
        // Windows 10 2004+: keep the recording controls out of captured frames.
        bool excluded = Native.ExcludeFromCapture(this);
        _outline.Open();
        var controls = new System.Drawing.Rectangle(x, y, (int)(Width * scale), (int)(Height * scale));
        if (!excluded && controls.IntersectsWith(_region))
        { _start.IsEnabled = false; _clock.Text = "请缩小选区"; }
    }
    private async Task Begin()
    {
        if (_running || _ending) return;
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(Preferences.FilePath)!, "recordings"); Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "recording-" + Guid.NewGuid().ToString("N") + ".gif");
            _writer = new AnimatedGifWriter(File.Create(_path)); _running = true; _watch.Start(); _previousFrame = 0;
            _outline.Recording(true);
            _start.IsEnabled = false; _pause.IsEnabled = _stop.IsEnabled = true;
            _frame = CaptureFrame(); await _frame; if (!_ending) _timer.Start();
        }
        catch (Exception ex) { await Fail(ex); }
    }
    private async Task CaptureFrame()
    {
        try
        {
            long elapsed = _watch.ElapsedMilliseconds; int delay = (int)Math.Clamp(elapsed - _previousFrame, FrameMilliseconds, 2000); _previousFrame = elapsed;
            bool includeCursor = _cursor.IsChecked == true;
            await Task.Run(() => { var image = ScreenPixels.Read(_region, 1280, includeCursor); _writer!.Add(image, delay); });
        }
        catch (Exception ex) { if (!_ending) _ = Fail(ex); }
    }
    private void Pause()
    {
        if (!_running || _ending) return; _paused = !_paused; _pause.Content = Ui.Label(_paused ? "play" : "pause", _paused ? "继续" : "暂停");
        if (_paused) _watch.Stop(); else _watch.Start();
        _outline.Recording(!_paused);
    }
    private async Task Stop()
    {
        if (_ending || !_running) return; _ending = true; _timer.Stop(); _watch.Stop();
        _start.IsEnabled = _pause.IsEnabled = _stop.IsEnabled = false;
        await _frame;
        int frames = _writer?.Frames ?? 0; var error = FinishWriter(); Close();
        if (frames > 0 && error == null) Completed?.Invoke(_path);
        else { DeleteTemporary(); Failed?.Invoke(error == null ? "未录到画面，请重试" : "GIF 写入失败：" + error.Message); }
    }
    private async Task Fail(Exception error)
    {
        if (_ending) return; _ending = true; _timer.Stop(); _watch.Stop();
        await Task.Yield(); await _frame;
        FinishWriter(); DeleteTemporary(); Close(); Failed?.Invoke("录制失败：" + error.Message);
    }
    public async void Cancel()
    {
        if (_ending) return; _ending = true; _timer.Stop(); _watch.Stop();
        await _frame; FinishWriter(); DeleteTemporary(); Close(); Canceled?.Invoke();
    }
    private Exception? FinishWriter()
    {
        try { _writer?.Dispose(); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex; }
        finally { _writer = null; }
    }
    private void DeleteTemporary() { try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}
