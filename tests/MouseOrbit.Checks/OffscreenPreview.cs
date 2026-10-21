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
    private static BitmapSource RenderMenu(ContextMenu menu)
    {
        menu.Measure(new Size(178, 400));
        return Render(menu, 178, (int)Math.Clamp(Math.Ceiling(menu.DesiredSize.Height), 1, 400));
    }
    private static BitmapSource OnBackdrop(BitmapSource overlay, bool dark)
    {
        var view = new DrawingVisual();
        using (var dc = view.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(24, 26, 29) : Color.FromRgb(250, 250, 248)), null, new Rect(0, 0, overlay.PixelWidth, overlay.PixelHeight));
            dc.DrawImage(overlay, new Rect(0, 0, overlay.PixelWidth, overlay.PixelHeight));
        }
        var bitmap = new RenderTargetBitmap(overlay.PixelWidth, overlay.PixelHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); return bitmap;
    }
    private static BitmapSource Board(BitmapSource panel, BitmapSource wheel, bool dark)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(51, 65, 77) : Color.FromRgb(192, 204, 212)), null, new Rect(0, 0, 1280, 800));
            // Crisp fixture work windows: overlay readability is judged on content, not a blurred wallpaper.
            dc.DrawImage(SampleWindow(3, dark), new Rect(55, 105, 745, 435));
            dc.DrawImage(SampleWindow(5, dark), new Rect(650, 250, 565, 330));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 30, 39, 46)), null, new Rect(0, 756, 1280, 44));
            dc.DrawImage(panel, new Rect(355, 198, panel.Width, panel.Height));
            dc.DrawImage(wheel, new Rect(690, 120, wheel.Width, wheel.Height));
            var pointer = Geometry.Parse("M0,0 L0,19 L5,14 L9,23 L13,21 L9,13 L16,13 Z");
            dc.PushTransform(new TranslateTransform(330, 375)); dc.DrawGeometry(Brushes.White, new Pen(Palette.B("#34434C"), 1), pointer); dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
    }
    private static Button Button(DependencyObject root, string name) => Descendants<Button>(root).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
    private static void Click(DependencyObject root, string name)
    {
        if (Window.GetWindow(root) is QuickPanelWindow rail && name is "窗口" or "常用" or "最近")
        { rail.SelectCategory(name == "窗口" ? "windows" : name == "常用" ? "frequent" : "recent"); return; }
        Button(root, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    }
    private static BitmapSource SampleWindow(int index, bool dark = false)
    {
        var view = new DrawingVisual();
        using (var dc = view.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(32, 38, 43) : Color.FromRgb(243, 244, 240)), null, new Rect(0, 0, 480, 280));
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(48, 55, 61) : Color.FromRgb(215, 221, 219)), null, new Rect(0, 0, 480, 27));
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(39, 47, 52) : Color.FromRgb(234, 235, 230)), null, new Rect(0, 27, 83, 253));
            var colors = new[] { "#577D93", "#65907D", "#B7865E", "#89719C", "#687A93", "#6F8D9A", "#9A8559", "#658B77" };
            var accent = (Brush)new BrushConverter().ConvertFromString(colors[index % colors.Length])!;
            dc.DrawRectangle(accent, null, new Rect(101, 45, 350, 48));
            for (int i = 0; i < 6; i++) dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(85, 97, 106) : Color.FromRgb(188, 194, 187)), null, new Rect(104, 113 + i * 22, i % 2 == 0 ? 325 : 252, 7));
            for (int i = 0; i < 5; i++) dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(91, 105, 111) : Color.FromRgb(168, 178, 168)), null, new Rect(13, 49 + i * 29, 54, 5));
        }
        var result = new RenderTargetBitmap(480, 280, 96, 96, PixelFormats.Pbgra32); result.Render(view); result.Freeze(); return result;
    }
    public static void Run()
    {
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        SpatialInteractionChecks.Run();
        AppearanceQualityChecks.Run();
        ParticleMotionChecks.Run();
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
        var wheelView = new OrbitVisual { Width = 420, Height = 420, Selected = 1 };
        Click(root, "最近");
        Render(root, (int)quick.Width, (int)quick.Height);
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
        var wheelItem = quick.CreateMenu().Items.OfType<MenuItem>().Single(item => item.Header as string == "设置").Items.OfType<MenuItem>().Single(item => item.Header as string == "轮盘");
        wheelItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); if (!wheelRequested) throw new Exception("Wheel remains available from mouse menu");
        foreach (var theme in ThemePresets.All)
        {
            Palette.Apply(theme.Id); quick.SetWindows(groups); quick.SetEntries(new() { ["frequent"] = apps, ["recent"] = apps });
            root = (FrameworkElement)quick.Content;
            wheelView.InvalidateVisual();
            Save(Render(root, (int)quick.Width, (int)quick.Height), "rail-" + theme.Id + ".png");
            foreach (bool dark in new[] { false, true })
            {
                Palette.SetBackdropDark(dark); root = (FrameworkElement)quick.Content; wheelView.InvalidateVisual();
                Save(OnBackdrop(Render(root, (int)quick.Width, (int)quick.Height), dark), "contrast-" + theme.Id + (dark ? "-dark.png" : "-light.png"));
                var hovered = Button(root, groups[Math.Min(2, groups.Length - 1)].Name);
                quick.SpatialView!.FocusRow(hovered, false);
                var hoverImage = Render(root, (int)quick.Width, (int)quick.Height);
                Save(OnBackdrop(hoverImage, dark), "particle-hover" + (dark ? "-dark.png" : "-light.png"));
                quick.SpatialView.HoldMenu(true);
                var wingImage = RenderMenu(quick.CreateMenu());
                hoverImage = Render(root, (int)quick.Width, (int)quick.Height);
                var composition = new DrawingVisual();
                using (var dc = composition.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(24, 26, 29) : Color.FromRgb(250, 250, 248)), null, new Rect(0, 0, 452, 424));
                    dc.DrawImage(hoverImage, new Rect(4, 8, hoverImage.Width, hoverImage.Height));
                    dc.DrawImage(wingImage, new Rect(248, 8 + quick.SpatialView.MenuAnchor.TransformToAncestor(root).Transform(new Point()).Y - 32, wingImage.Width, wingImage.Height));
                }
                var composite = new RenderTargetBitmap(452, 424, 96, 96, PixelFormats.Pbgra32); composite.Render(composition);
                Save(composite, "particle-wing" + (dark ? "-dark.png" : "-light.png"));
                quick.SpatialView.HoldMenu(false); quick.SpatialView.FocusRow(hovered, false, false);
                Save(OnBackdrop(Render(wheelView, 420, 420), dark), "contrast-wheel-" + theme.Id + (dark ? "-dark.png" : "-light.png"));
                Save(Board(Render(root, (int)quick.Width, (int)quick.Height), Render(wheelView, 420, 420), dark), "scene-" + theme.Id + (dark ? "-dark.png" : "-light.png"));
            }
            Palette.SetBackdropDark(false); root = (FrameworkElement)quick.Content; wheelView.InvalidateVisual();
            var menuPreview = quick.CreateMenu();
            Save(RenderMenu(menuPreview), "menu-" + theme.Id + ".png");
            Save(Board(Render(root, (int)quick.Width, (int)quick.Height), Render(wheelView, 420, 420), theme.Dark), "theme-" + theme.Id + ".png");
            if (theme.Layout != AppearanceLayout.Classic)
            {
                var closed = Render(root, (int)quick.Width, (int)quick.Height);
                Click(root, groups[0].Name);
                var frame = new DispatcherFrame(); var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; }; settle.Start(); Dispatcher.PushFrame(frame);
                var expanded = Render(root, (int)quick.Width, (int)quick.Height);
                Save(expanded, "interaction-" + theme.Id + "-expanded.png");
                using (var gif = new AnimatedGifWriter(File.Create(Path.Combine(Output, "interaction-" + theme.Id + ".gif")))) { gif.Add(closed, 900); gif.Add(expanded, 1500); }
                Click(root, groups[0].Name);
            }
            if (Palette.Surface.Color.A >= 245 || Palette.Ink.Color.A != 255) throw new Exception("Theme must keep background translucent and text solid: " + theme.Id);
        }
        // A matched five-app fixture makes the material comparison independent of installed app names.
        var studyNames = new[] { ("Chrome", "Chrome"), ("Edge", "Edge"), ("微信", "微信"), ("Code", "Code"), ("资源", "文件") };
        var studyGroups = studyNames.Select(pair => groups.FirstOrDefault(g => g.Name.Contains(pair.Item1, StringComparison.OrdinalIgnoreCase)) is { } found ? found with { Name = pair.Item2 } : null).OfType<SwitchGroup>().ToArray();
        if (studyGroups.Length >= 3)
        foreach (bool dark in new[] { false, true })
        {
            Palette.SetBackdropDark(dark); quick.SetWindows(studyGroups); root = (FrameworkElement)quick.Content;
            Render(root, (int)quick.Width, (int)quick.Height);
            quick.SpatialView!.FocusRow(Button(root, studyGroups[2].Name), false);
            quick.SpatialView.HoldMenu(true);
            var panelImage = Render(root, (int)quick.Width, (int)quick.Height);
            var wingImage = RenderMenu(quick.CreateMenu());
            var image = new DrawingVisual();
            using (var dc = image.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(24, 26, 29) : Color.FromRgb(250, 250, 248)), null, new Rect(0, 0, 452, 360));
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, 452, 360)));
                dc.DrawImage(panelImage, new Rect(4, 8, panelImage.Width, panelImage.Height));
                dc.DrawImage(wingImage, new Rect(248, 8 + quick.SpatialView.MenuAnchor.TransformToAncestor(root).Transform(new Point()).Y - 32, wingImage.Width, wingImage.Height)); dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(452, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(image);
            Save(bitmap, "particle-fidelity" + (dark ? "-dark.png" : "-light.png"));
        }
        ParticleMotionPreview.Write(groups, apps, Output);
        string referenceFile = Path.Combine(Environment.CurrentDirectory, "output/design-concepts/2026-10-20-particle/虚幻粒子-形态预览.png");
        if (File.Exists(referenceFile) && File.Exists(Path.Combine(Output, "particle-fidelity-light.png")))
        {
            BitmapImage Read(string path) { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image; }
            var reference = Read(referenceFile); int third = reference.PixelWidth / 3;
            var referenceCrop = new CroppedBitmap(reference, new Int32Rect(third * 2, 0, reference.PixelWidth - third * 2, reference.PixelHeight));
            var actual = Read(Path.Combine(Output, "particle-fidelity-light.png"));
            var board = new DrawingVisual();
            using (var dc = board.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1040, 780));
                void Title(string label, double x) => dc.DrawText(new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Palette.BrandFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 18, Palette.B("#303638"), 1), new Point(x, 22));
                Title("概念参考", 24); Title("实际控件 · 原生尺寸", 552);
                dc.DrawImage(referenceCrop, new Rect(20, 75, referenceCrop.PixelWidth * .8, referenceCrop.PixelHeight * .8));
                dc.DrawImage(actual, new Rect(550, 160, actual.PixelWidth, actual.PixelHeight));
            }
            var bitmap = new RenderTargetBitmap(1040, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(board); Save(bitmap, "particle-reference-comparison.png");
        }
        Palette.SetBackdropDark(false); quick.SetWindows(groups);
        Palette.Apply("typefield"); root = (FrameworkElement)quick.Content;
        if (quick.CreateMenu().Items.OfType<MenuItem>().Any(item => item.Header as string == "样式")) throw new Exception("Single-style menu must not offer a style picker");
        Palette.SetTheme(false);
        quick.SetWindows(groups); root = (FrameworkElement)quick.Content;
        Save(Render(root, (int)quick.Width, (int)quick.Height), "pixel-compact.png");
        foreach (string name in new[] { "更多" })
        {
            var button = Button(root, name); var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
            if (bounds.Left < 0 || bounds.Right > quick.Width) throw new Exception("Compact toolbar extends beyond screen: " + name);
        }

        var main = new MainWindow(new Preferences());
        ((ListBox)main.FindName("ResultsList")).ItemsSource = apps; ((ListBox)main.FindName("ResultsList")).Tag = "frequent";
        Save(Render((FrameworkElement)main.Content, 1040, 740), "manager-typefield.png");
        ((Grid)main.FindName("AssistantPanel")).Visibility = Visibility.Collapsed; ((Grid)main.FindName("SettingsPanel")).Visibility = Visibility.Visible;
        Save(Render((FrameworkElement)main.Content, 1040, 740), "settings-typefield.png");
        Save(Render((FrameworkElement)main.Content, 940, 650), "settings-minimum.png");
        foreach (var combo in Descendants<ComboBox>((DependencyObject)main.Content))
        {
            if (combo.Visibility != Visibility.Visible || combo.ActualWidth == 0) continue;
            var bounds = combo.TransformToAncestor((Visual)main.Content).TransformBounds(new Rect(combo.RenderSize));
            if (bounds.Left < 0 || bounds.Right > 940) throw new Exception("Setting field extends beyond minimum window width");
        }
        Palette.SetTheme(true); ((OrbitVisual)main.FindName("Preview")).InvalidateVisual(); Save(Render((FrameworkElement)main.Content, 1040, 740), "settings-typefield-dark.png");
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
        Console.WriteLine("PASS: compact icon rail, light/dark, native icons, recent/frequent removal, mouse menu, scaled capture; offscreen renders saved to " + Output);
    }
}
