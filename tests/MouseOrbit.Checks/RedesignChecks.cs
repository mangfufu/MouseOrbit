using MouseOrbit;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class RedesignChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        string Stroke(IEnumerable<StrokePoint> points)
        {
            var samples = points.ToArray(); var gesture = new StrokeGesture(samples[0].X, samples[0].Y, 20);
            for (int i = 1; i < samples.Length; i++) gesture.Add(samples[i].X, samples[i].Y, i * 5);
            return gesture.Recognize();
        }
        StrokePoint[] Arc(double from, double sweep, double scale = 1, double jitter = 0) => Enumerable.Range(0, 100)
            .Select(i => { double a = from + sweep * i / 99; return new StrokePoint(410 + Math.Cos(a) * 100 * scale + Math.Sin(i * 3.1) * jitter, -220 + Math.Sin(a) * 100 * scale + Math.Cos(i * 1.3) * jitter); }).ToArray();
        foreach (double scale in new[] { .6, 1, 1.8 })
        {
            var c = Arc(-Math.PI / 4, -Math.PI * 1.5, scale, 1.2);
            Check(Stroke(c) == "C", "open C continuous, scaled " + scale);
            Check(Stroke(c.Reverse()) == "C", "reverse C " + scale);
            Check(Stroke(Arc(0, Math.PI * 2, scale, 1.2)) == "O", "clockwise closed circle " + scale);
            Check(Stroke(Arc(0, -Math.PI * 2, scale, 1.2)) == "O", "counterclockwise closed circle " + scale);
            var v = new[] { new StrokePoint(0, 0), new StrokePoint(70 * scale, 120 * scale), new StrokePoint(140 * scale, 0) };
            Check(Stroke(v) == "V", "V continuous " + scale);
            Check(Stroke(v.Reverse()) == "V", "reverse V " + scale);
        }
        Check(Stroke(new[] { new StrokePoint(0, 0), new StrokePoint(-100, 0), new StrokePoint(-100, -90) }) == "", "old LU no longer copies or snaps");
        Check(Stroke(new[] { new StrokePoint(0, 0), new StrokePoint(50, 70) }) == "", "ambiguous diagonal ignored");
        Check(Stroke(Arc(0, Math.PI * 2, .10)) == "", "tiny loop ignored");
        Check(Stroke(Arc(0, Math.PI * 4)) == "", "double spiral not a single circle");
        var legacy = Preferences.Normalize(new Preferences { Trigger = "XButton2", GesturesMap = new()
        { ["L"] = "back", ["R"] = "forward", ["U"] = "top", ["D"] = "bottom", ["UD"] = "refresh", ["DR"] = "closeTab", ["DU"] = "newTab", ["LU"] = "reopen" } });
        Check(legacy.Trigger == "XButton2" && legacy.GesturesMap["L"] == "snapLeft" && legacy.GesturesMap["C"] == "copy", "original browser defaults migrate without changing side key");
        var custom = Preferences.Normalize(new Preferences { GesturesMap = new() { ["L"] = "topmost", ["R"] = "snapRight" } });
        Check(custom.GesturesMap["L"] == "topmost" && custom.GesturesMap["V"] == "paste", "custom cardinal mapping survives with new shape defaults");
        custom.GesturesMap["C"] = "cut";
        Check(Preferences.Normalize(custom).GesturesMap["C"] == "cut", "new-version custom C is preserved");
        foreach (var (x, y, result) in new[] { (-100d, 0d, "L"), (100d, 0d, "R"), (0d, -100d, "U"), (0d, 100d, "D") })
            Check(Stroke(new[] { new StrokePoint(0, 0), new StrokePoint(x, y) }) == result, "straight " + result);
        var volume = new StrokeGesture(0, 0, 20); volume.Add(80, 0, 100);
        Check(!volume.TryEnterVolume(), "right stroke alone remains window snap");
        volume.Add(80, -36, 101); Check(volume.TryEnterVolume(), "right then up enters volume immediately without pause");
        Check(volume.TakeVolumeSteps() == 3, "volume up continuous");
        volume.Add(80, 12, 370); Check(volume.TakeVolumeSteps() == -4, "volume down continuous");
        Check(volume.Recognize() == "", "volume mode never executes right snap on release");
        var curving = new StrokeGesture(0, 0); curving.Add(80, 60, 100); Check(!curving.TryEnterVolume(), "curved stroke cannot arm volume");
        var downVolume = new StrokeGesture(0, 0); downVolume.Add(80, 0, 1); downVolume.Add(80, 24, 2);
        Check(downVolume.TryEnterVolume() && downVolume.TakeVolumeSteps() == -2, "right then down needs no stationary sample");
        var roundedVolume = new StrokeGesture(0, 0); roundedVolume.Add(80, 0, 1);
        foreach (int i in Enumerable.Range(1, 20)) { double a = Math.PI / 2 - Math.PI / 2 * i / 20; roundedVolume.Add(80 + 20 * Math.Cos(a), -20 + 20 * Math.Sin(a), i + 1); }
        roundedVolume.Add(100, -60, 22);
        Check(roundedVolume.TryEnterVolume() && roundedVolume.TakeVolumeSteps() > 0, "natural rounded volume corner works continuously");
        foreach (double scale in new[] { .6, 1, 1.8, 4 })
        foreach (var sample in new[] { Arc(-Math.PI / 2, Math.PI * 2, scale, 1.2), Arc(-Math.PI / 2, -Math.PI * 2, scale, 1.2), Arc(-Math.PI / 4, -Math.PI * 1.5, scale, 1.2) })
        {
            var shape = new StrokeGesture(sample[0].X, sample[0].Y, 20); bool volumeEntered = false;
            for (int i = 1; i < sample.Length; i++) { shape.Add(sample[i].X, sample[i].Y, i); volumeEntered |= shape.TryEnterVolume(); }
            Check(!volumeEntered, "every prefix of C/circle keeps shape interpretation " + scale);
        }

        var press = new TriggerPress(); press.Begin(1000, false);
        Check(!press.TryHold(1219, 0, 0) && press.End(1219, 0, 0) == TriggerRelease.Panel, "tap opens panel only on release");
        Check(press.End(1300, 0, 0) == TriggerRelease.None, "duplicate up cannot open second surface");
        press.Begin(2000, false);
        Check(press.TryHold(2220, 0, 0) && !press.TryHold(2240, 0, 0), "hold timer opens wheel once at 220ms");
        Check(press.End(2300, 100, 0) == TriggerRelease.Wheel, "hold release selects wheel instead of panel");
        press.Begin(3000, false);
        Check(press.End(3300, 0, 0) == TriggerRelease.Wheel, "delayed timer cannot turn a hold into panel");
        press.Begin(4000, false);
        Check(press.TryHold(4020, 80, 0) && press.End(4060, 80, 0) == TriggerRelease.Wheel, "fast flick beyond wheel dead zone keeps original wheel path");
        foreach (var motion in new[] { new Point(12, 0), new Point(40, 0), new Point(30, 30), new Point(-40, -20) })
        {
            press.Begin(4100, false);
            Check(!press.TryHold(4140, motion.X, motion.Y) && press.End(4200, motion.X, motion.Y) == TriggerRelease.Panel,
                "side-button tap with incidental movement opens panel: " + motion);
        }
        Check(TriggerButtons.Matches("XButton1", 0x20B, 1u << 16) && !TriggerButtons.Matches("XButton1", 0x20B, 2u << 16), "back side button remains distinct from forward side button");
        Check(!TriggerButtons.Matches("XButton2", 0x20C, 2u << 16) && !TriggerButtons.Matches("XButton2", 0x207, 0), "side-button binding ignores releases and middle button");
        Check(TriggerButtons.Matches("XButton2", 0x20B, 2u << 16) && !TriggerButtons.Matches("XButton2", 0x20B, 1u << 16), "explicit forward-button binding remains exclusive");
        Check(TriggerButtons.Matches("Middle", 0x207, 0) && !TriggerButtons.Matches("Middle", 0x20B, 1u << 16), "explicit middle-button binding remains exclusive");
        press.Begin(5000, false); press.TryHold(5300, 80, 0); int beforeCancel = press.Version; press.Cancel();
        Check(press.Version != beforeCancel && press.End(5400, 80, 0) == TriggerRelease.None, "right click or focus cancellation cannot reopen on release");
        press.Begin(6000, true); Check(press.End(6100, 0, 0) == TriggerRelease.None, "tap on open panel dismisses");
        press.Begin(6500, false); press.Cancel(); Check(!press.TryHold(6800, 80, 0) && press.End(6900, 80, 0) == TriggerRelease.None, "cancel before hold threshold suppresses both surfaces");
        press.Begin(7000, true); Check(press.TryHold(7220, 0, 0) && press.End(7300, 100, 0) == TriggerRelease.Wheel, "hold from open panel still opens wheel");
        var path = new PointerPath(); path.Begin(new(-1920, -200)); path.Add(new(-1919, -200));
        Check(path.Points.Count == 1, "trail ignores subpixel jitter");
        for (int i = 1; i < 4000; i++) path.Add(new(-1920 + i * 3, -200 + i));
        Check(path.Points.Count <= 2048 && path.Points[0] == new Point(-1920, -200) && path.Points[^1].X == -1920 + 3999 * 3, "trail retains endpoints across screens with bounded memory");
        path.Clear(); Check(path.Points.Count == 0, "release/cancel clears trail");
        Check(ThemePresets.All.Select(t => t.Id).Distinct().Count() == 6 && ThemePresets.All.Any(t => t.Id == "glass"), "six styles retain original glass theme");
        Check(ThemePresets.Find("unknown", true).Id == "pixel-dark", "old dark preference survives theme upgrade");
        foreach (string name in new[] { "Progman", "WorkerW", "#32769", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            Check(WindowNative.IsDesktopClass(name), "desktop surface excluded from automatic full-screen focus: " + name);
        Check(!WindowNative.IsDesktopClass("CabinetWClass") && !WindowNative.IsDesktopClass("UnrealWindow"), "normal folders and games remain eligible for auto focus");
        var shell = WindowNative.GetShellWindow();
        if (shell != 0)
            Check(!WindowNative.GameFocused(shell, new Preferences { FocusApplications = [WindowNative.ProcessPath(shell)] }), "real desktop cannot cancel a stroke through automatic focus, even with explorer listed");
        var migratedWheel = Preferences.Normalize(new Preferences { Wheel = ["assistant", "taskView", "explorer", "downloads", "desktop", "screenshot", "settings", "switchApp"] });
        Check(migratedWheel.Wheel.SequenceEqual(new Preferences().Wheel) && Preferences.PageNames.Length == 1, "original three-page default upgrades to curated one-page wheel");
        Check(!new Preferences().Wheel.Contains("restore") && !new Preferences().Wheel.Contains("centerWindow") && new Preferences().Wheel.Contains("screenshot"), "single-page defaults remove redundant restore/center and retain shared capture");

        var windows = new[] { new SwitchWindow(1, 7, "chrome", "Chrome", "current", false, 1, 90), new SwitchWindow(2, 7, "chrome", "Chrome", "recent", true, 2, 80), new SwitchWindow(3, 7, "chrome", "Chrome", "older", false, 1, 40), new SwitchWindow(4, 8, "code", "Code", "editor", false, 1, 70) };
        var groups = WindowCatalog.Group(windows, 1);
        Check(groups.Count == 2 && groups[0].Key == "chrome" && groups[0].Windows.Count == 2, "group apps and exclude active window");
        Check(groups[0].Recent.Handle == 2 && groups[0].Recent.Minimized && groups[0].Recent.Screen == 2, "recent minimized cross-screen window retained");
        Check(WindowCatalog.Group(new[] { windows[0] }, 1).Count == 0, "current-only group disappears");
        foreach (var area in new[] { new Rect(0, 0, 1920, 1040), new Rect(-2560, -300, 2560, 1400), new Rect(0, 0, 800, 540) })
        {
            foreach (var p in new[] { area.TopLeft, area.BottomRight, new Point(area.Right - 1, area.Top + 1) })
            { var rect = PanelPlacement.Place(p.X, p.Y, 840 * 1.5, 544 * 1.5, area); Check(area.Contains(rect), "panel clamped to working area " + area); }
        }

        var image = Solid(120, 90, Colors.White);
        var document = new CaptureDocument(image); document.Add(new("rectangle", new(10, 10), new(70, 70), Colors.Red, 4));
        Check(Pixel(document.Render(), 10, 30).R > 220 && Pixel(document.Render(), 10, 30).G < 40, "rectangle uses chosen color and stroke width");
        Check(Pixel(document.Render(), 1, 1) == Colors.White, "annotations stay in image area");
        document.Undo(); Check(Pixel(document.Render(), 10, 30) == Colors.White, "annotation undo");
        document.Redo(); Check(Pixel(document.Render(), 10, 30).G < 40, "annotation redo");
        document.Add(new("ellipse", new(80, 10), new(110, 40), Colors.Blue, 3));
        document.Add(new("text", new(15, 35), new(15, 35), Colors.Black, 3, 16, "测试"));
        Check(document.Render().PixelWidth == 120 && document.Render().PixelHeight == 90, "text and ellipse render at source resolution");

        using var memory = new MemoryStream();
        var stream = new KeepOpenStream(memory);
        using (var gif = new AnimatedGifWriter(stream)) { gif.Add(Solid(32, 24, Colors.Red), 120); gif.Add(Solid(32, 24, Colors.Lime), 240); Check(gif.Frames == 2, "GIF writes two frames"); }
        memory.Position = 0;
        var decoder = new GifBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Check(decoder.Frames.Count == 2 && decoder.Frames[0].PixelWidth == 32, "exported GIF decodes as animation");
        Check(Pixel(decoder.Frames[0], 10, 10).R > 220 && Pixel(decoder.Frames[1], 10, 10).G > 220, "GIF frame content preserved");
        Check(Convert.ToInt32(((BitmapMetadata)decoder.Frames[0].Metadata).GetQuery("/grctlext/Delay")) == 12 && Convert.ToInt32(((BitmapMetadata)decoder.Frames[1].Metadata).GetQuery("/grctlext/Delay")) == 24, "GIF timing metadata preserved");
        Directory.CreateDirectory("output/qa"); File.WriteAllBytes("output/qa/recording-fixture.gif", memory.ToArray());
        Console.WriteLine($"PASS: {count} redesign checks (strokes, targets, placement, markup, GIF; no foreground input)");
    }
    internal static BitmapSource Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = color.A; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static Color Pixel(BitmapSource source, int x, int y)
    { var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); byte[] value = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), value, 4, 0); return Color.FromArgb(value[3], value[2], value[1], value[0]); }
    private sealed class KeepOpenStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => inner.CanWrite; public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c); public override long Seek(long o, SeekOrigin s) => inner.Seek(o, s); public override void SetLength(long v) => inner.SetLength(v); public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
    }
}
