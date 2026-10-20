using MouseOrbit;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class DesktopChecks
{
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    public static void Run()
    {
        int count = 0;
        void Check(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); count++; }
        var fixture = new Window { Title = "MouseOrbit verification fixture", Width = 500, Height = 350, ShowActivated = false, Left = 100, Top = 100 };
        fixture.Show(); Pump();
        var handle = new WindowInteropHelper(fixture).Handle;
        try
        {
            Check(DesktopActions.Execute(Commands.Find("maximize"), handle), "maximize API accepted"); Pump();
            Check(fixture.WindowState == WindowState.Maximized, "window maximized");
            Check(DesktopActions.Execute(Commands.Find("restore"), handle), "restore API accepted"); Pump();
            Check(fixture.WindowState == WindowState.Normal, "window restored");
            Check(DesktopActions.Execute(Commands.Find("minimize"), handle), "minimize API accepted"); Pump();
            Check(fixture.WindowState == WindowState.Minimized, "window minimized");
            DesktopActions.Execute(Commands.Find("restore"), handle); Pump();
            WindowNative.GetWindowRect(handle, out var original);
            Check(DesktopActions.Execute(Commands.Find("snapLeft"), handle), "left half screen"); Pump();
            Check(DesktopActions.Execute(Commands.Find("snapLeft"), handle), "same left gesture exits half screen"); Pump();
            WindowNative.GetWindowRect(handle, out var restored);
            Check(Near(original, restored), "left toggle restores original size and position");
            DesktopActions.Execute(Commands.Find("snapLeft"), handle); Pump();
            Check(DesktopActions.Execute(Commands.Find("snapRight"), handle), "right half screen"); Pump();
            DesktopActions.Execute(Commands.Find("snapRight"), handle); Pump();
            WindowNative.GetWindowRect(handle, out restored);
            Check(Near(original, restored), "switching left to right retains original restore position");
            DesktopActions.Execute(Commands.Find("maximize"), handle); Pump();
            DesktopActions.Execute(Commands.Find("snapLeft"), handle); Pump();
            DesktopActions.Execute(Commands.Find("snapLeft"), handle); Pump();
            Check(fixture.WindowState == WindowState.Maximized, "unsnapping a previously maximized window restores maximized state");
            DesktopActions.Execute(Commands.Find("restore"), handle); Pump();
            var snapArea = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            Native.SetWindowPos(handle, 0, snapArea.Left, snapArea.Top, snapArea.Width / 2, snapArea.Height, 0x14); Pump();
            DesktopActions.Execute(Commands.Find("snapLeft"), handle); Pump();
            WindowNative.GetWindowRect(handle, out restored);
            Check(Math.Abs((restored.Right - restored.Left) - snapArea.Width / 2) > 20, "a pre-existing half-screen window can exit half screen without a saved placement");
            Check(DesktopActions.Execute(Commands.Find("topmost"), handle), "pin window");
            Check((Native.GetWindowLong(handle, -20) & 8) != 0, "topmost flag set");
            Check(DesktopActions.IsTopmost(handle), "pinned state is observable when opening the wheel");
            DesktopActions.Execute(Commands.Find("topmost"), handle);
            Check((Native.GetWindowLong(handle, -20) & 8) == 0, "topmost flag cleared");
            Check(!DesktopActions.IsTopmost(handle), "unpinning updates the observable state");
            using (var toast = new ActionToast())
            {
                var before = Native.GetForegroundWindow();
                toast.NotifyPinned(true, new Native.Point { X = 600, Y = 400 }); Pump();
                Check(toast.IsVisible && ((System.Windows.Controls.TextBlock)((System.Windows.Controls.Border)toast.Content).Child).Text.Contains("已置顶"), "pin action has a visible confirmation");
                toast.NotifyPinned(false, new Native.Point { X = 600, Y = 400 }); Pump();
                Check(((System.Windows.Controls.TextBlock)((System.Windows.Controls.Border)toast.Content).Child).Text.Contains("已取消置顶"), "unpin confirmation is distinct");
                Check(Native.GetForegroundWindow() == before, "pin feedback preserves foreground and keyboard focus");
            }
            Check(DesktopActions.Execute(Commands.Find("centerWindow"), handle), "center window"); Pump();
            var foreground = Native.GetForegroundWindow();
            var orbit = new OrbitWindow();
            try
            {
                orbit.Open(new Native.Point { X = 600, Y = 400 }, new Preferences()); Pump();
                Check(orbit.IsVisible, "overlay visible");
                Check(Native.GetForegroundWindow() == foreground, "overlay preserves foreground");
                var center = orbit.CenterPixels;
                orbit.Update(new Native.Point { X = (int)center.X, Y = (int)(center.Y - 100 * orbit.Scale) });
                Check(orbit.Visual.Selected == 0, "overlay north sector");
                orbit.Update(new Native.Point { X = (int)center.X, Y = (int)center.Y });
                Check(orbit.Visual.Selected == -1, "overlay center cancellation");
            }
            finally { orbit.Close(); }
            using (var hint = new HintScope())
            {
                var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                var point = new Native.Point { X = area.Right - 1, Y = area.Bottom - 1 };
                var target = WindowTarget.Capture(handle);
                hint.Window.Update(point, target, "左半屏"); Pump();
                // Settle initial WPF sizing/DPI before observing subsequent updates.
                hint.Window.Update(point, target, "左半屏"); Pump();
                int positionChanges = 0;
                var hintHandle = hint.Window.Handle;
                using var source = new PositionCounter(hintHandle, () => positionChanges++);
                try
                {
                    for (int i = 0; i < 30; i++) hint.Window.Update(point, target, "左半屏");
                    Pump();
                    Check(positionChanges == 0, "stationary hint at screen edge does not bounce or repeatedly reposition");
                    Check(WindowNative.GetWindowRect(hintHandle, out var box) && box.Right <= area.Right && box.Bottom <= area.Bottom,
                        "hint remains inside work area at bottom-right edge");
                    var before = Native.GetForegroundWindow();
                    var other = new Window { Width = 100, Height = 100, Topmost = true, ShowActivated = false };
                    other.Show(); Pump();
                    try
                    {
                        nint previous = GetWindow(hintHandle, 3);
                        Native.SetWindowPos(hintHandle, 0, box.Left - 2, box.Top - 2, 0, 0, 0x0215); Pump();
                        Check(GetWindow(hintHandle, 3) == previous, "moving a gesture hint preserves overlay stacking order");
                        Check(Native.GetForegroundWindow() == before, "gesture hint updates preserve foreground window");
                    }
                    finally { other.Close(); }
                }
                finally { source.ReleaseHandle(); }
            }
            var quick = new QuickPanelWindow();
            var item = new DesktopEntry("fixture", "测试应用", "本地测试", "app", "unused", "□");
            DesktopEntry? selected = null;
            quick.Selected += entry => selected = entry;
            try
            {
                var before = Native.GetForegroundWindow();
                quick.Open(new Native.Point { X = 600, Y = 400 }, new()
                {
                    ["frequent"] = new[] { item, new DesktopEntry("fixture2", "下载文件夹", "系统操作", "command", "downloads", "↓"), new DesktopEntry("fixture3", "浏览器", "应用", "app", "unused", "□") },
                    ["recent"] = Array.Empty<DesktopEntry>(), ["windows"] = Array.Empty<DesktopEntry>()
                });
                Pump();
                Check(quick.IsVisible, "nearby panel opens");
                Check(Native.GetForegroundWindow() == before, "nearby panel preserves foreground");
                Check(quick.Contains(new Native.Point { X = 600, Y = 400 }), "nearby panel is placed around pointer");
                IEnumerable<DependencyObject> Descendants(DependencyObject node)
                {
                    for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                        if (child is UIElement element && element.Visibility != Visibility.Visible) continue;
                        yield return child;
                        foreach (var nested in Descendants(child)) yield return nested;
                    }
                }
                Check(!Descendants(quick).OfType<System.Windows.Controls.TextBox>().Any(), "nearby panel has no keyboard input field");
                Descendants(quick).OfType<System.Windows.Controls.Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "常用")
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                var screenshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)quick.Width, (int)quick.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                screenshot.Render(quick);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(screenshot));
                using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "mouseorbit-nearby-qa.png"))) png.Save(stream);
                var button = Descendants(quick).OfType<System.Windows.Controls.Button>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == item.Name);
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(selected == item && !quick.IsVisible, "single mouse click selects target and closes panel");
            }
            finally { quick.Close(); }
            var explorer = new DesktopEntry("icon-fixture", "Explorer", "", "app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "");
            var realIcon = ShellIcons.Load(explorer).GetAwaiter().GetResult();
            Check(realIcon is System.Windows.Media.Imaging.BitmapSource && realIcon.IsFrozen, "native executable icon loads and crosses threads safely");
            var folder = explorer with { Kind = "folder", Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            Check(ShellIcons.Load(folder).GetAwaiter().GetResult() != null, "native folder icon loads");
            Check(ShellIcons.Load(explorer with { Target = "Z:\\MouseOrbit-missing-fixture.exe" }).GetAwaiter().GetResult() == null, "missing application icon falls back without crashing");
            // Capture only the generated test window, through the real recording controls.
            fixture.Topmost = true; fixture.Width = 500; fixture.Height = 350; fixture.Left = 100; fixture.Top = 100; Pump();
            WindowNative.GetWindowRect(handle, out var captureBounds);
            var region = new System.Drawing.Rectangle(captureBounds.Left + 50, captureBounds.Top + 70, 240, 140);
            var recorder = new GifRecorderWindow(region);
            string? recording = null; string? failure = null;
            var recordingFrame = new DispatcherFrame();
            recorder.Completed += path => { recording = path; recordingFrame.Continue = false; };
            recorder.Failed += message => { failure = message; recordingFrame.Continue = false; };
            var stopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            void ClickRecorder(string name) => DescendantButtons((DependencyObject)recorder.Content)
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            stopTimer.Tick += (_, _) => { stopTimer.Stop(); ClickRecorder("结束并预览"); };
            timeout.Tick += (_, _) => { timeout.Stop(); failure = "recording timed out"; recorder.Cancel(); recordingFrame.Continue = false; };
            try
            {
                recorder.Open(); ClickRecorder("开始录制"); stopTimer.Start(); timeout.Start(); Dispatcher.PushFrame(recordingFrame);
                Check(failure == null && recording != null, "recording completes through real start/stop controls: " + failure);
                using (var animation = new GifAnimation(recording!))
                { Check(animation.Count >= 3 && animation.Frame(0).PixelWidth == 240 && animation.Frame(0).PixelHeight == 140, "live capture produces multiple frames with selected dimensions"); }
                var preview = new GifPreviewWindow(recording!, region);
                int finished = 0; preview.Done += () => finished++;
                try
                {
                    preview.Open(); Pump();
                    Check(preview.Width <= 640 && preview.Height <= 460 && preview.IsVisible, "recording opens in a compact nearby preview");
                }
                finally { preview.Close(); }
                Check(finished == 1 && !File.Exists(recording), "preview completion fires once and cleans up the recording");
            }
            finally { stopTimer.Stop(); timeout.Stop(); recorder.Cancel(); fixture.Topmost = false; }
        }
        finally { fixture.Close(); }
        Console.WriteLine($"PASS: {count} Windows integration checks (only test windows manipulated)");
    }
    private static bool Near(WindowNative.Box a, WindowNative.Box b) => Math.Abs(a.Left - b.Left) <= 3 && Math.Abs(a.Top - b.Top) <= 3 && Math.Abs(a.Right - b.Right) <= 3 && Math.Abs(a.Bottom - b.Bottom) <= 3;
    private static IEnumerable<System.Windows.Controls.Button> DescendantButtons(DependencyObject node)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
            if (child is System.Windows.Controls.Button button) yield return button;
            foreach (var nested in DescendantButtons(child)) yield return nested;
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);
    private sealed class HintScope : IDisposable
    {
        public GestureHint Window { get; } = new();
        public void Dispose() => Window.Dispose();
    }
    private sealed class PositionCounter : System.Windows.Forms.NativeWindow, IDisposable
    {
        private readonly Action _changed;
        public PositionCounter(nint handle, Action changed) { _changed = changed; AssignHandle(handle); }
        protected override void WndProc(ref System.Windows.Forms.Message message) { if (message.Msg == 0x47) _changed(); base.WndProc(ref message); }
        public void Dispose() => ReleaseHandle();
    }
}
