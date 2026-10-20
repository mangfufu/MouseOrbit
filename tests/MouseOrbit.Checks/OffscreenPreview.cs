using MouseOrbit;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Every image uses real controls rendered without Show(), global hooks, or foreground input.
internal static class OffscreenPreview
{
    private static readonly string Output = Path.Combine(Environment.CurrentDirectory, "output", "qa");
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is UIElement element && element.Visibility != Visibility.Visible) continue;
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    internal static BitmapSource Render(FrameworkElement view, int width, int height)
    {
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); Pump();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); bitmap.Freeze(); return bitmap;
    }
    private static void Save(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Output, name)); encoder.Save(stream);
    }
    private static BitmapSource Board(BitmapSource panel, BitmapSource wheel, bool dark)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(19, 19, 17) : Color.FromRgb(222, 217, 204)), null, new Rect(0, 0, 1400, 720));
            dc.DrawImage(panel, new Rect(32, 86, panel.Width, panel.Height));
            dc.DrawImage(wheel, new Rect(934, 145, wheel.Width, wheel.Height));
        }
        var bitmap = new RenderTargetBitmap(1400, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
    }
    private static Button Button(DependencyObject root, string name) => Descendants<Button>(root).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
    private static void Click(DependencyObject root, string name) => Button(root, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static BitmapSource SampleWindow(int index)
    {
        var view = new DrawingVisual();
        using (var dc = view.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(243, 244, 240)), null, new Rect(0, 0, 480, 280));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(215, 221, 219)), null, new Rect(0, 0, 480, 27));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(234, 235, 230)), null, new Rect(0, 27, 83, 253));
            var colors = new[] { "#577D93", "#65907D", "#B7865E", "#89719C", "#687A93", "#6F8D9A", "#9A8559", "#658B77" };
            var accent = (Brush)new BrushConverter().ConvertFromString(colors[index % colors.Length])!;
            dc.DrawRectangle(accent, null, new Rect(101, 45, 350, 48));
            for (int i = 0; i < 6; i++) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(188, 194, 187)), null, new Rect(104, 113 + i * 22, i % 2 == 0 ? 325 : 252, 7));
            for (int i = 0; i < 5; i++) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(168, 178, 168)), null, new Rect(13, 49 + i * 29, 54, 5));
        }
        var result = new RenderTargetBitmap(480, 280, 96, 96, PixelFormats.Pbgra32); result.Render(view); result.Freeze(); return result;
    }
    public static void Run()
    {
        _ = new Application();
        Directory.CreateDirectory(Output); Palette.SetTheme(false);
        var launcher = new Launcher(); launcher.Refresh();
        string[] preferred = ["Chrome", "Edge", "微信", "Visual Studio Code", "Windows Terminal", "网易云"];
        var apps = preferred.Select(name => launcher.Entries.FirstOrDefault(e => e.Kind == "app" && e.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .OfType<DesktopEntry>().Concat(new[] {
                new DesktopEntry("explorer", "文件资源管理器", "", "app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), ""),
                new DesktopEntry("notepad", "记事本", "", "app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32", "notepad.exe"), "")
            }).DistinctBy(e => e.Target).Take(8).ToArray();
        System.Threading.Tasks.Task.WhenAll(apps.Select(ShellIcons.Load)).GetAwaiter().GetResult();
        if (!apps.Any(e => ShellIcons.Load(e).Result is BitmapSource)) throw new Exception("No native application icons loaded");
        var groups = apps.Select((app, i) => new SwitchGroup(app.Id, app.ShortName,
            Enumerable.Range(0, i == 0 ? 3 : i == 2 ? 2 : 1)
                .Select(n => new SwitchWindow(0, 0, app.Id, app.ShortName, app.ShortName + " · " + (n + 1), i == 4, i == 2 ? 2 : 1, 100 - i - n)
                { IconEntry = app, Preview = SampleWindow(i + n) }).ToArray())).ToArray();
        var quick = new QuickPanelWindow(); quick.SetWindows(groups);
        quick.SetEntries(new() { ["frequent"] = apps, ["recent"] = apps, ["windows"] = [] });
        var root = (FrameworkElement)quick.Content;
        var panel = Render(root, (int)quick.Width, (int)quick.Height);
        if (Descendants<Button>(root).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "GIF")) throw new Exception("Panel must have one shared capture entry");
        if (!Descendants<Image>(root).Any(image => image.Source is BitmapSource source && source.PixelWidth == 480)) throw new Exception("Window thumbnails missing");
        Save(panel, "pixel-panel-light.png");
        var wheelView = new OrbitVisual { Width = 420, Height = 420, Selected = 1 };
        var wheel = Render(wheelView, 420, 420);
        Save(Board(panel, wheel, false), "pixel-light.png");
        Click(root, groups[0].Name);
        Save(Render((FrameworkElement)quick.Content, (int)quick.Width, (int)quick.Height), "pixel-expanded.png");
        if (Button(quick.Content as DependencyObject ?? quick, "返回应用").Visibility != Visibility.Visible) throw new Exception("Group must expand inline with a back action");
        Palette.SetTheme(true); root = (FrameworkElement)quick.Content;
        Click(root, "返回应用"); panel = Render(root, (int)quick.Width, (int)quick.Height);
        wheelView.InvalidateVisual(); wheel = Render(wheelView, 420, 420);
        Save(panel, "pixel-panel-dark.png"); Save(Board(panel, wheel, true), "pixel-dark.png");
        Palette.SetTheme(false); root = (FrameworkElement)quick.Content;
        Click(root, "最近"); Save(Render(root, (int)quick.Width, (int)quick.Height), "pixel-recent.png");
        bool opened = false; int removed = 0; quick.Selected += _ => opened = true;
        quick.RemoveRecentRequested = _ => { removed++; return true; };
        Descendants<Button>(root).First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("从最近移除：")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Render(root, (int)quick.Width, (int)quick.Height);
        if (opened || removed != 1 || Descendants<EntryIcon>(root).Count() != apps.Length - 1) throw new Exception("Recent remove must not launch target");
        Click(root, "常用"); quick.RemoveFrequentRequested = _ => true;
        Render(root, (int)quick.Width, (int)quick.Height);
        Descendants<Button>(root).First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("从常用移除：")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Render(root, (int)quick.Width, (int)quick.Height);
        if (Descendants<EntryIcon>(root).Count() != apps.Length - 1) throw new Exception("Frequent remove must update immediately");
        bool wheelRequested = false; quick.ToolRequested += id => wheelRequested = id == "wheel";
        Click(root, "轮盘"); if (!wheelRequested) throw new Exception("Wheel must be available in fixed toolbar");
        foreach (var theme in ThemePresets.All)
        {
            Palette.Apply(theme.Id); quick.SetWindows(groups); quick.SetEntries(new() { ["frequent"] = apps, ["recent"] = apps });
            root = (FrameworkElement)quick.Content;
            wheelView.InvalidateVisual();
            Save(Board(Render(root, 840, 544), Render(wheelView, 420, 420), theme.Dark), "theme-" + theme.Id + ".png");
            if (Palette.Surface.Color.A >= 245 || Palette.Ink.Color.A != 255) throw new Exception("Theme must keep background translucent and text solid: " + theme.Id);
        }
        Palette.Apply("glass"); root = (FrameworkElement)quick.Content; Click(root, "选择主题");
        Save(Render(root, 840, 544), "theme-picker.png");
        string selectedTheme = ""; quick.ToolRequested += id => { if (id.StartsWith("theme:")) selectedTheme = id[6..]; };
        Click(root, "半透明 · 鼠尾草"); if (selectedTheme != "sage") throw new Exception("Theme picker must be mouse operable");
        Palette.SetTheme(false);
        quick.Width = 390; quick.Height = 430; quick.SetWindows(groups); root = (FrameworkElement)quick.Content;
        Save(Render(root, 390, 430), "pixel-compact.png");
        foreach (string name in new[] { "窗口", "最近", "常用", "选择主题", "专注模式", "关闭", "轮盘", "截图", "管理" })
        {
            var button = Button(root, name); var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
            if (bounds.Left < 0 || bounds.Right > 390) throw new Exception("Compact toolbar extends beyond screen: " + name);
        }

        var main = new MainWindow(new Preferences());
        ((ListBox)main.FindName("ResultsList")).ItemsSource = apps; ((ListBox)main.FindName("ResultsList")).Tag = "frequent";
        Save(Render((FrameworkElement)main.Content, 1040, 740), "pixel-manager-light.png");
        ((Grid)main.FindName("AssistantPanel")).Visibility = Visibility.Collapsed; ((Grid)main.FindName("SettingsPanel")).Visibility = Visibility.Visible;
        Save(Render((FrameworkElement)main.Content, 1040, 740), "pixel-settings-light.png");
        Palette.SetTheme(true); ((OrbitVisual)main.FindName("Preview")).InvalidateVisual(); Save(Render((FrameworkElement)main.Content, 1040, 740), "pixel-settings-dark.png");
        Palette.SetTheme(false);
        var screenshot = SampleWindow(2);
        var capture = new CaptureOverlay(screenshot, new System.Drawing.Rectangle(0, 0, 480, 280));
        capture.Width = 1024; capture.Height = 700;
        // A full-size fixture background lets the actual selection toolbar be inspected.
        screenshot = RedesignChecks.Solid(1024, 700, Color.FromRgb(216, 222, 215));
        capture.Close(); capture = new CaptureOverlay(screenshot, new System.Drawing.Rectangle(0, 0, 1024, 700));
        System.Drawing.Rectangle? gifRegion = null; capture.GifRegion += region => gifRegion = region;
        Render((FrameworkElement)capture.Content, 1024, 700);
        capture.Surface.SelectRegion(new Rect(80, 70, 700, 410));
        if (gifRegion != null) throw new Exception("Selection must keep screenshot editor open before choosing GIF");
        capture.Surface.Document!.Add(new("rectangle", new(40, 50), new(280, 210), Colors.OrangeRed, 4));
        capture.Surface.Document.Add(new("ellipse", new(350, 70), new(540, 240), Colors.DodgerBlue, 3));
        capture.Surface.Document.Add(new("text", new(60, 260), new(60, 260), Colors.Black, 3, 24, "区域截图与标注"));
        Save(Render((FrameworkElement)capture.Content, 1024, 700), "pixel-capture.png");
        Save(capture.Surface.Document.Render(), "annotated-fixture.png");
        Click((DependencyObject)capture.Content, "GIF");
        if (gifRegion != new System.Drawing.Rectangle(80, 70, 700, 410)) throw new Exception("GIF button must reuse selected screenshot region");
        var scaledCapture = new CaptureOverlay(RedesignChecks.Solid(800, 600, Colors.White), new System.Drawing.Rectangle(-2560, -300, 800, 600)) { Width = 400, Height = 300 };
        scaledCapture.Surface.Scale = 2; System.Drawing.Rectangle? scaledGifRegion = null;
        scaledCapture.GifRegion += region => scaledGifRegion = region;
        Render((FrameworkElement)scaledCapture.Content, 400, 300);
        scaledCapture.Surface.SelectRegion(new Rect(20, 10, 100, 60));
        Render((FrameworkElement)scaledCapture.Content, 400, 300); Click((DependencyObject)scaledCapture.Content, "GIF");
        if (scaledGifRegion != new System.Drawing.Rectangle(-2520, -280, 200, 120)) throw new Exception("Shared GIF selection must preserve negative monitor origin and DPI");
        scaledCapture.Close();
        var scaled = new CaptureSurface(RedesignChecks.Solid(400, 240, Colors.White)) { Scale = 2 };
        scaled.SelectRegion(new Rect(20, 10, 100, 60));
        if (scaled.Document!.Image.PixelWidth != 200 || scaled.Document.Image.PixelHeight != 120) throw new Exception("Scaled crop must preserve physical pixels");
        if (!Palette.PixelFont.GetTypefaces().Any(f => f.TryGetGlyphTypeface(out _))) throw new Exception("Embedded pixel font did not load");
        var trace = new PointerTrailVisual { Origin = new(-1920, -200), Scale = 2, Points = new Point[] { new(-1900, -180), new(-1820, -100), new(-1740, -180) } };
        var traceImage = Render(trace, 120, 90); Save(traceImage, "pointer-trail.png");
        var tracePixel = new byte[4]; traceImage.CopyPixels(new Int32Rect(50, 50, 1, 1), tracePixel, 4, 0);
        if (tracePixel[3] < 200) throw new Exception("Scaled cross-screen trail must render");
        trace.Points = []; trace.InvalidateVisual(); traceImage = Render(trace, 120, 90);
        traceImage.CopyPixels(new Int32Rect(50, 50, 1, 1), tracePixel, 4, 0);
        if (tracePixel[3] != 0) throw new Exception("Cleared trail must leave no pixels");
        var recordingOutline = new RecordingOutline(new System.Drawing.Rectangle(0, 0, 300, 180)); recordingOutline.Recording(true);
        var borderImage = Render((FrameworkElement)recordingOutline.Content, 300, 180); Save(borderImage, "recording-outline.png");
        borderImage.CopyPixels(new Int32Rect(0, 80, 1, 1), tracePixel, 4, 0);
        if (tracePixel[3] != 255) throw new Exception("GIF recording area must have a visible border");
        borderImage.CopyPixels(new Int32Rect(150, 80, 1, 1), tracePixel, 4, 0);
        if (tracePixel[3] != 0) throw new Exception("GIF border must leave recording content unobstructed");
        recordingOutline.Close();
        var hidden = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("MouseOrbit hidden identity fixture")
        { ParentWindow = new nint(-3), WindowStyle = 0, Width = 1, Height = 1 });
        var target = WindowTarget.Capture(hidden.Handle);
        if (!target.Valid || target.ClipboardReady() || (target with { Process = 0 }).Valid || (target with { Thread = 0 }).Valid)
            throw new Exception("Window target identity/focus guard failed");
        hidden.Dispose(); if (target.Valid) throw new Exception("Closed target must be rejected");
        capture.Close(); main.Close(); quick.Close();
        Console.WriteLine("PASS: grouped window UI, thumbnails, light/dark, native icons, recent/frequent removal, wheel entry, scaled capture; offscreen renders saved to " + Output);
    }
}
