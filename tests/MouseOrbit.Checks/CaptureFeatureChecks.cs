using MouseOrbit;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class CaptureFeatureChecks
{
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var image = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes;
    }
    private static Color Pixel(BitmapSource bitmap, int x, int y)
    { var bytes = Pixels(bitmap); int i = (y * bitmap.PixelWidth + x) * 4; return Color.FromArgb(bytes[i + 3], bytes[i + 2], bytes[i + 1], bytes[i]); }
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        string themeBefore = Palette.Current.Id;
        foreach (var theme in ThemePresets.All)
        {
            Palette.Apply(theme.Id);
            var visual = new OrbitVisual { Width = 420, Height = 420 };
            var plain = OffscreenPreview.Render(visual, 420, 420); var before = Pixels(plain);
            visual.Selected = 1; visual.InvalidateVisual(); var selected = OffscreenPreview.Render(visual, 420, 420); var after = Pixels(selected);
            Check(Pixel(selected, 210, 210).A == 0 && Pixel(selected, 210, 150).A == 0, "wheel center and gaps remain fully transparent: " + theme.Id);
            Check(Pixel(selected, 230, 80).A is > 200 and < 255 && Pixel(selected, 285, 90).A == Pixel(plain, 285, 90).A, "tiles stay translucent at the same alpha on hover: " + theme.Id);
            Check(Enumerable.Range(0, before.Length / 4).All(i => before[i * 4 + 3] == after[i * 4 + 3]), "hover never changes wheel transparency or geometry: " + theme.Id);
            Check(!before.SequenceEqual(after), "hover changes wheel color: " + theme.Id);
            if (theme.Id == "typefield") ScreenPixels.SavePng(selected, "output/qa/wheel-translucent.png");
            visual.TargetPinned = true; visual.InvalidateVisual(); var pinned = OffscreenPreview.Render(visual, 420, 420);
            Check(!Pixels(pinned).SequenceEqual(after), "pinned target has a visible wheel state: " + theme.Id);
            if (theme.Id == "typefield") ScreenPixels.SavePng(pinned, "output/qa/wheel-pinned.png");
        }
        Palette.Apply(themeBefore);
        var first = RedesignChecks.Solid(160, 90, Colors.White); var second = RedesignChecks.Solid(160, 90, Colors.Blue);
        var surface = new CaptureSurface(first) { Width = 160, Height = 90 };
        surface.SelectRegion(new Rect(0, 0, 160, 90));
        var mark = new Markup("arrow", new(20, 30), new(120, 30), Colors.Red, 5);
        surface.Document!.Add(mark); surface.ShowFrame(second);
        Check(surface.Document.Marks.Count == 1 && Pixel(surface.Document.Render(), 60, 30).R > 220, "animation frame changes preserve visible markup");
        surface.Document.Undo(); Check(Pixel(surface.Document.Render(), 60, 30).B > 220, "GIF markup can be undone on a different frame");
        surface.Document.Redo(); Check(Pixel(surface.Document.Render(), 60, 30).R > 220, "GIF markup redo survives frame changes");
        string folder = Path.Combine(Path.GetTempPath(), "MouseOrbit-capture-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string input = Path.Combine(folder, "input.gif"), output = Path.Combine(folder, "annotated.gif");
            using (var writer = new AnimatedGifWriter(File.Create(input))) { writer.Add(first, 50); writer.Add(second, 130); }
            using (var animation = new GifAnimation(input))
            {
                Check(animation.Count == 2 && animation.Delay(0) == 50 && animation.Delay(1) == 130, "recording frame timing is readable");
                var previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                try
                {
                    Task export = ExportBoth();
                    async Task ExportBoth()
                    {
                        await animation.Export(Path.Combine(folder, "unchanged.gif"), Array.Empty<Markup>());
                        await animation.Export(output, new[] { mark });
                    }
                    var frame = new DispatcherFrame();
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    _ = export.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                    Dispatcher.PushFrame(frame); export.GetAwaiter().GetResult();
                }
                finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            }
            Check(File.ReadAllBytes(input).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "unchanged.gif"))), "saving without annotations preserves original recording bytes");
            using (var exported = new GifAnimation(output))
            {
                Check(exported.Count == 2 && exported.Delay(0) == 50 && exported.Delay(1) == 130, "annotated export preserves frame count and exact timing");
                Check(Pixel(exported.Frame(0), 60, 30).R > 200 && Pixel(exported.Frame(1), 60, 30).R > 200, "annotations are burned into every GIF frame");
                Check(Pixel(exported.Frame(0), 5, 5).R > 200 && Pixel(exported.Frame(1), 5, 5).B > 200, "annotated export retains underlying animation");
                Check(exported.Frame(0).PixelWidth == 160 && exported.Frame(0).PixelHeight == 90, "annotated export preserves dimensions");
            }
            Check(!Directory.EnumerateFiles(folder, ".mouseorbit-*").Any(), "successful GIF export leaves no temporary file");
            string previewPath = Path.Combine(folder, "preview.gif"); File.Copy(input, previewPath);
            var preview = new GifPreviewWindow(previewPath, new(100, 100, 320, 180));
            try
            {
                var root = (FrameworkElement)preview.Content;
                OffscreenPreview.Render(root, 520, 360);
                ButtonFor(root, "暂停并标注整段 GIF").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(preview.Surface.IsHitTestVisible, "GIF annotation mode enables drawing");
                Check(Descendants<Button>(root).Any(b => b.Visibility == Visibility.Visible && System.Windows.Automation.AutomationProperties.GetName(b) == "箭头"), "GIF editor exposes annotation tools");
                ScreenPixels.SavePng(OffscreenPreview.Render(root, 520, 420), "output/qa/gif-editor.png");
            }
            finally { preview.Close(); }
            Check(!File.Exists(previewPath), "closing preview releases and removes its temporary recording");
            using var bitmap = new System.Drawing.Bitmap(80, 80);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Magenta);
                ScreenCursor.DrawAt(graphics, new(-1920, -100, 80, 80), System.Windows.Forms.Cursors.Arrow.Handle, new Native.Point { X = -1900, Y = -80 });
            }
            Check(Enumerable.Range(15, 45).Any(x => Enumerable.Range(15, 45).Any(y => bitmap.GetPixel(x, y).ToArgb() != System.Drawing.Color.Magenta.ToArgb())), "cursor is drawn at capture-relative coordinates on a negative-origin monitor");
            Check(bitmap.GetPixel(2, 2).ToArgb() == System.Drawing.Color.Magenta.ToArgb(), "cursor drawing does not paint outside its bounds");
            var recorder = new GifRecorderWindow(new(100, 100, 320, 180));
            try
            {
                var cursor = Descendants<CheckBox>((DependencyObject)recorder.Content).Single();
                Check(cursor.IsChecked == true && (string?)cursor.Content == "显示鼠标", "recording enables cursor visibility by default");
                cursor.IsChecked = false; Check(cursor.IsChecked == false, "cursor visibility can be disabled");
                ScreenPixels.SavePng(OffscreenPreview.Render((FrameworkElement)recorder.Content, (int)recorder.Width, (int)recorder.Height), "output/qa/gif-recorder.png");
            }
            finally { recorder.Cancel(); }
        }
        finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
        Console.WriteLine($"PASS: {count} capture feature checks (translucent wheel, pin state, GIF annotation/export, cursor)");
    }
    private static Button ButtonFor(DependencyObject root, string name) => Descendants<Button>(root).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i); if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
