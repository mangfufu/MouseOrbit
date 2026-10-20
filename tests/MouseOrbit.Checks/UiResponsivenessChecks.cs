using MouseOrbit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static class UiResponsivenessChecks
{
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Visible<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is UIElement element && element.Visibility != Visibility.Visible) continue;
            if (child is T value) yield return value;
            foreach (var nested in Visible<T>(child)) yield return nested;
        }
    }
    internal static void Run()
    {
        System.IO.Directory.CreateDirectory("output/qa");
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        var panel = new QuickPanelWindow();
        var entries = Enumerable.Range(0, 40).Select(i => new DesktopEntry("perf:" + i, "项目 " + i, "", "command", "downloads", "↓")).ToArray();
        var snapshot = new Dictionary<string, IReadOnlyList<DesktopEntry>> { ["frequent"] = entries[..24], ["recent"] = entries };
        Button Button(string name) => Visible<Button>(panel).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
        void Click(string name) { Button(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump(); }
        try
        {
            panel.Open(new Native.Point { X = 400, Y = 300 }, snapshot); Pump(); Click("常用");
            var card = Button("项目 0"); Click("最近"); Click("常用");
            Check(ReferenceEquals(card, Button("项目 0")), "switching tabs reuses existing card controls");
            var shell = panel.Content; panel.Hide(); panel.Open(new Native.Point { X = 400, Y = 300 }, snapshot); Pump(); Click("常用");
            Check(ReferenceEquals(shell, panel.Content) && ReferenceEquals(card, Button("项目 0")), "reopening unchanged panel preserves shell and page cache");
            panel.Hide();
            var panelHandle = new WindowInteropHelper(panel).Handle;
            Check(!panel.IsVisible && WindowNative.GetAncestor(WindowNative.WindowFromPoint(new Native.Point { X = 450, Y = 350 }), 2) != panelHandle,
                "keeping the prepared presentation surface does not leave a hidden mouse-blocking window");
            panel.Open(new Native.Point { X = 400, Y = 300 }, snapshot); Pump(); Click("常用");
            var retainedCard = Button("项目 1");
            snapshot["frequent"] = entries[1..24]; panel.Hide(); panel.Open(new Native.Point { X = 400, Y = 300 }, snapshot); Pump(); Click("常用");
            Check(!Visible<Button>(panel).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "项目 0"), "external data changes invalidate cached pages");
            Check(ReferenceEquals(retainedCard, Button("项目 1")), "removing one entry preserves every unchanged card instead of rebuilding the page");
            var added = new DesktopEntry("added", "新应用", "", "command", "downloads", ""); snapshot["frequent"] = snapshot["frequent"].Prepend(added).ToArray();
            panel.Hide(); panel.Open(new Native.Point { X = 400, Y = 300 }, snapshot); Pump(); Click("常用");
            Check(ReferenceEquals(retainedCard, Button("项目 1")) && Button("新应用") != null, "reopening after adding an application creates only the new card");
            var groups = Enumerable.Range(0, 8).Select(i => new SwitchGroup("group:" + i, "应用组 " + i,
                new[] { new SwitchWindow(0, 0, "group:" + i, "应用", "窗口 " + i, false, 1, i) { Preview = RedesignChecks.Solid(20, 20, Colors.Gray) } })).ToArray();
            panel.SetWindows(groups); Click("窗口"); var windowCard = Button("应用组 1"); panel.Hide();
            groups[0] = groups[0] with { Name = "变化的应用" }; panel.SetWindows(groups); panel.Open(new Native.Point { X = 400, Y = 300 }, snapshot); Pump();
            Check(ReferenceEquals(windowCard, Button("应用组 1")) && Button("变化的应用") != null, "changed window data preserves unrelated window cards when the side-button panel reopens");
            var iconEntry = new DesktopEntry("cached-icon", "已读取图标", "", "app", Environment.ProcessPath!, "");
            ShellIcons.PreloadAsync(new[] { iconEntry }).GetAwaiter().GetResult();
            Check(ShellIcons.TryGet(iconEntry, out var prepared) && prepared != null && ReferenceEquals(prepared, new EntryIcon { Entry = iconEntry }.Source), "preloaded application icons are assigned synchronously with no deferred UI repaint");
        }
        finally { panel.Close(); }
        var manager = new MainWindow(new Preferences());
        try
        {
            var list = (ListBox)manager.FindName("ResultsList");
            list.ItemsSource = Enumerable.Range(0, 800).Select(i => new DesktopEntry("app:" + i, "虚拟应用 " + i, "", "command", "downloads", "↓")).ToArray();
            OffscreenPreview.Render((FrameworkElement)manager.Content, 1040, 740);
            var tiles = Visible<TileVirtualizingPanel>(list).Single();
            int Realized() => Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null);
            Check(Realized() is > 0 and < 80, "800 apps create only viewport containers");
            tiles.SetVerticalOffset(double.PositiveInfinity); manager.UpdateLayout(); Pump();
            Check(list.ItemContainerGenerator.ContainerFromIndex(799) != null && Realized() < 80, "virtualized app list scrolls to last item with bounded containers");
            tiles.SetVerticalOffset(0); manager.UpdateLayout(); Pump();
            Check(list.ItemContainerGenerator.ContainerFromIndex(0) != null, "recycled list can scroll back to first item");
            list.ItemsSource = entries[..5]; manager.UpdateLayout(); Pump();
            Check(Realized() == 5 && tiles.ExtentHeight <= tiles.ViewportHeight, "changing categories clears old containers and clamps scroll extent");
            ScreenPixels.SavePng(OffscreenPreview.Render((FrameworkElement)manager.Content, 1040, 740), "output/qa/virtualized-manager.png");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var launcher = (Launcher)typeof(MainWindow).GetField("_launcher", flags)!.GetValue(manager)!;
            ((List<DesktopEntry>)launcher.Entries).AddRange(Enumerable.Range(0, 800).Select(i => new DesktopEntry("cached:" + i, "应用 " + i, "", "app", "unused", "")));
            typeof(MainWindow).GetField("_lastIndexRefresh", flags)!.SetValue(manager, Environment.TickCount64);
            manager.PrepareView();
            void Category(string key)
            {
                Visible<Button>((DependencyObject)manager.Content).Single(b => b.Tag as string == key).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                manager.UpdateLayout(); Pump();
            }
            Category("apps"); var appView = Visible<ListBox>((DependencyObject)manager.Content).Single();
            var firstCard = appView.ItemContainerGenerator.ContainerFromIndex(0);
            Category("recent"); Check(Visible<ListBox>((DependencyObject)manager.Content).Single().Items.Count == 0, "empty category has its own viewport without stale app cards");
            Category("apps");
            Check(ReferenceEquals(appView, Visible<ListBox>((DependencyObject)manager.Content).Single()) && ReferenceEquals(firstCard, appView.ItemContainerGenerator.ContainerFromIndex(0)), "manager reuses realized app cards across category switches");
            ((Button)manager.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); manager.UpdateLayout(); Pump();
            Check(((Grid)manager.FindName("SettingsPanel")).IsMeasureValid && ((Grid)manager.FindName("AssistantPanel")).Visibility == Visibility.Hidden, "settings navigation retains the prepared library layout");
            ((Button)manager.FindName("LibraryNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Category("apps");
            Check(ReferenceEquals(firstCard, appView.ItemContainerGenerator.ContainerFromIndex(0)), "opening settings and returning preserves virtualized app containers");
            var addedApplication = new DesktopEntry("cached-new", "000 新应用", "", "app", "unused", "");
            ((List<DesktopEntry>)launcher.Entries).Add(addedApplication); Category("apps");
            Check(appView.Items.Count == 801 && ReferenceEquals(firstCard, appView.ItemContainerGenerator.ContainerFromIndex(1)), "discovering an app inserts a card without resetting the 800-app view");
            ((List<DesktopEntry>)launcher.Entries).Remove(addedApplication); Category("apps");
            Check(appView.Items.Count == 800 && ReferenceEquals(firstCard, appView.ItemContainerGenerator.ContainerFromIndex(0)), "removing an app preserves unrelated realized app containers");
        }
        finally { manager.Close(); }
        var screen = new System.Drawing.Rectangle(-1920, -100, 1920, 1080);
        var stroke = new Rect(-1850, 30, 130, 80); var bounds = PointerTrail.SurfaceBounds(stroke, screen);
        Check(bounds.Contains(stroke) && bounds.Width * bounds.Height < screen.Width * screen.Height / 8d, "short gesture allocates a small surface on negative-origin screen");
        Check(PointerTrail.SurfaceBounds(new Rect(100, 100, 20, 20), screen).IsEmpty, "unvisited monitors allocate no stroke surface");
        // Reproduce startup order: the overlay exists before an application is pinned.
        using var preparedSurface = new LayeredSurface("MouseOrbit prepared overlay verification");
        var fixture = new Window { Width = 400, Height = 300, Left = 100, Top = 100, ShowActivated = false, Topmost = true, Background = Brushes.White };
        fixture.Show(); Pump();
        try
        {
            var window = new WindowInteropHelper(fixture).Handle; WindowNative.GetWindowRect(window, out var rect);
            var point = new Native.Point { X = rect.Left + 80, Y = rect.Top + 80 };
            var thumbnailWindow = new QuickPanelWindow();
            try
            {
                thumbnailWindow.SetWindows(Enumerable.Range(0, 16).Select(i => new SwitchGroup("live:" + i, "实时预览 " + i,
                    new[] { new SwitchWindow(window, (uint)Environment.ProcessId, "live:" + i, "测试", "源窗口", false, 1, i) })).ToArray());
                thumbnailWindow.Open(new Native.Point { X = 600, Y = 300 }, new()); Pump();
                var tile = Visible<Button>(thumbnailWindow).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "实时预览 0");
                var location = tile.PointToScreen(new(50, 35));
                bool White(Point at)
                {
                    using var pixelImage = new System.Drawing.Bitmap(1, 1); var until = Environment.TickCount64 + 500; System.Drawing.Color sample;
                    do { Pump(); DwmFlush(); Capture(pixelImage, (int)at.X, (int)at.Y); sample = pixelImage.GetPixel(0, 0); }
                    while ((sample.R < 245 || sample.G < 245 || sample.B < 245) && Environment.TickCount64 < until);
                    return sample.R >= 245 && sample.G >= 245 && sample.B >= 245;
                }
                Check(White(location), "real window cards display DWM live thumbnails on the translucent panel");
                Visible<Button>(thumbnailWindow).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "最近").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                Check(!White(location), "changing pages removes native previews along with their WPF cards");
                Visible<Button>(thumbnailWindow).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "窗口").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                var viewport = Visible<ScrollViewer>(thumbnailWindow).Single(); viewport.ScrollToBottom(); thumbnailWindow.UpdateLayout(); Pump();
                var last = Visible<Button>(thumbnailWindow).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "实时预览 15");
                Check(White(last.PointToScreen(new(50, 35))), "scrolling moves live thumbnails to the visible window cards");
                thumbnailWindow.Hide(); Pump();
            }
            finally { thumbnailWindow.Close(); }
            bool Above(nint overlay)
            {
                for (nint next = GetWindow(overlay, 3); next != 0; next = GetWindow(next, 3)) if (next == window) return false;
                return true;
            }
            var focus = Native.GetForegroundWindow();
            preparedSurface.Paint(new Rect(point.X, point.Y, 80, 60), graphics => graphics.Clear(System.Drawing.Color.Red)); Pump();
            Check(Above(preparedSurface.Handle), "pre-created overlay rises above a subsequently pinned application");
            preparedSurface.Hide(); Native.SetWindowPos(window, new nint(-1), 0, 0, 0, 0, 0x13);
            preparedSurface.Show(); Pump();
            Check(Above(preparedSurface.Handle) && Native.GetForegroundWindow() == focus, "each gesture raises the overlay without stealing focus");
            DwmFlush();
            using (var capture = new System.Drawing.Bitmap(1, 1))
            {
                Capture(capture, point.X + 20, point.Y + 20);
                var pixel = capture.GetPixel(0, 0);
                // Desktop capture can lag the DWM frame in a remote session.
                var until = Environment.TickCount64 + 500;
                while (pixel.G >= 60 && Environment.TickCount64 < until) { Pump(); DwmFlush(); Capture(capture, point.X + 20, point.Y + 20); pixel = capture.GetPixel(0, 0); }
                Check(pixel.R > 200 && pixel.G < 60, "overlay pixels are actually visible above a pinned application: " + pixel);
            }
            preparedSurface.Hide();
            using (var outline = new WindowOutline())
            {
                outline.Show(rect, Colors.Lime); Pump(); DwmFlush();
                Check(WindowNative.GetAncestor(WindowNative.WindowFromPoint(point), 2) == window, "hollow native outline leaves the application's interior interactive");
                Check(WindowNative.GetAncestor(WindowNative.WindowFromPoint(new Native.Point { X = rect.Left + 1, Y = point.Y }), 2) == window, "native outline edges pass mouse input through to the application");
                using var border = new System.Drawing.Bitmap(1, 1); var until = Environment.TickCount64 + 500;
                System.Drawing.Color pixel;
                do { Pump(); DwmFlush(); Capture(border, rect.Left + 1, point.Y); pixel = border.GetPixel(0, 0); }
                while ((pixel.G < 180 || pixel.R > 80) && Environment.TickCount64 < until);
                Check(pixel.G > 180 && pixel.R < 80, "native region draws the target frame without layered bitmap uploads");
                outline.Hide();
            }
            using (var trail = new PointerTrail())
            {
                trail.Prepare(); trail.Path.Begin(new(point.X, point.Y)); trail.Path.Add(new(point.X + 60, point.Y)); trail.Refresh(); Pump();
                DwmFlush(); using var capture = new System.Drawing.Bitmap(80, 30);
                Capture(capture, point.X, point.Y - 15);
                bool colored = Enumerable.Range(5, 50).Any(x => { var pixel = capture.GetPixel(x, 15); return Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) > 25; });
                Check(colored, "gesture trajectory is visible in the desktop composite over a pinned window");
                capture.Save("output/qa/topmost-gesture-trail.png"); trail.Clear();
            }
            using (var surface = new LayeredSurface("MouseOrbit paint verification"))
            {
                var before = Native.GetForegroundWindow();
                var area = new Rect(point.X, point.Y, 80, 60);
                surface.Paint(area, graphics => { using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(160, 220, 40, 20)); graphics.FillRectangle(brush, 10, 10, 40, 30); });
                var pixels = surface.Snapshot(); var alpha = new byte[4]; pixels.CopyPixels(new Int32Rect(20, 20, 1, 1), alpha, 4, 0);
                Check(alpha[3] == 160 && alpha[2] > alpha[1] && alpha[2] <= alpha[3], "native surface preserves premultiplied translucent color");
                pixels.CopyPixels(new Int32Rect(2, 2, 1, 1), alpha, 4, 0); Check(alpha[3] == 0, "native surface clears unused pixels to transparent");
                var root = WindowNative.GetAncestor(WindowNative.WindowFromPoint(new Native.Point { X = point.X + 20, Y = point.Y + 20 }), 2);
                Check(root == window, "native gesture surface passes mouse hit testing to underlying window");
                Check(Native.GetForegroundWindow() == before, "native surface preserves keyboard focus");
                surface.Paint(new Rect(point.X, point.Y, 24, 16), graphics => graphics.Clear(System.Drawing.Color.Blue));
                Check(surface.PixelArea == 24 * 16 && surface.Snapshot().PixelWidth == 24, "resizing native surface replaces backing buffer safely");
                surface.Hide(); Check(!WindowNative.IsWindowVisible(surface.Handle), "clearing native overlay hides it immediately");
            }
            using var hint = new GestureHint(); hint.Prepare();
            Check(!WindowNative.IsWindowVisible(hint.Handle), "preparing gesture graphics does not show a popup");
            hint.Update(point, WindowTarget.Capture(window), "左半屏");
            ScreenPixels.SavePng(hint.Snapshot(), "output/qa/native-gesture-hint.png");
        }
        finally { fixture.Close(); }
        Console.WriteLine($"PASS: {count} UI responsiveness checks (page reuse, virtualized scrolling, native surfaces)");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool BitBlt(nint dc, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    private static void Capture(System.Drawing.Bitmap bitmap, int x, int y)
    {
        using var graphics = System.Drawing.Graphics.FromImage(bitmap); var destination = graphics.GetHdc(); var source = GetDC(0);
        try { BitBlt(destination, 0, 0, bitmap.Width, bitmap.Height, source, x, y, 0x40CC0020); }
        finally { ReleaseDC(0, source); graphics.ReleaseHdc(destination); }
    }
}
