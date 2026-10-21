using MouseOrbit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class AppearanceQualityChecks
{
    private static Color Over(Color front, Color back)
    {
        double a = front.A / 255d;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return Color.FromRgb(Mix(front.R, back.R), Mix(front.G, back.G), Mix(front.B, back.B));
    }
    private static double Luminance(Color color)
    {
        double Linear(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private static double Contrast(Color a, Color b)
    { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bgra.PixelWidth * bgra.PixelHeight * 4]; bgra.CopyPixels(bytes, bgra.PixelWidth * 4, 0); return bytes;
    }
    internal static void Run()
    {
        int count = 0; string original = Palette.Current.Id;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; }
        foreach (var theme in ThemePresets.All)
        foreach (bool dark in new[] { false, true })
        {
            Palette.SetBackdropDark(dark);
            Palette.Apply(theme.Id);
            Check(Palette.Current.Id == theme.Id && Palette.Dark == dark && ThemePresets.All.Length == 1, "adaptive tone preserves the sole appearance identity");
            foreach (var background in new[] { Colors.Black, Colors.White })
            {
                var surface = Over(Palette.Surface.Color, background);
                Check(Contrast(Palette.Ink.Color, surface) >= 4.5 && Contrast(Palette.Muted.Color, surface) >= 4.5, "text remains readable over both black and white desktops: " + theme.Id + " / " + background);
                Check(Contrast(Palette.Ink.Color, Over(SpatialSurfaces.Selected.Color, background)) >= 4.5, "selected state retains text contrast: " + theme.Id);
            }
            if (theme.Layout == AppearanceLayout.Typefield)
            {
                var window = new SwitchWindow(0, 0, "test", "很长的应用窗口名称", "窗口", false, 1, 10);
                var panel = new QuickPanelWindow();
                panel.SetWindows(new[] { new SwitchGroup("test", "很长的应用窗口名称", new[] { window }), new SwitchGroup("second", "第二个应用", new[] { window }) });
                var bitmap = OffscreenPreview.Render((FrameworkElement)panel.Content, 254, 408); byte[] pixels = Pixels(bitmap);
                byte Alpha(int x, int y) => pixels[(y * 254 + x) * 4 + 3];
                Check(Alpha(190, 67) < 12 && Alpha(70, 67) < 12, "particle rows leave the space between targets clear");
                Check(Alpha(70, 136) < 5 && Alpha(10, 136) < 5, "particle UI has no continuous reading slab");
                var view = panel.SpatialView!;
                view.FocusRow(((StackPanel)view.Flow.Content).Children.OfType<Button>().First(), false);
                byte[] highlighted = Pixels(OffscreenPreview.Render((FrameworkElement)panel.Content, 254, 376));
                Check(Enumerable.Range(210, 23).Sum(x => Enumerable.Range(18, 32).Count(y => highlighted[(y * 254 + x) * 4 + 3] >= 8)) >= 8 &&
                    Enumerable.Range(18, 32).All(y => highlighted[(y * 254 + 243) * 4 + 3] < 8),
                    "the particle tail extends through the option and feathers out before its boundary");
                panel.Close();
                var name = "长名称窗口 Chrome";
                var protectedLabel = Pixels(OffscreenPreview.Render(new ParticleLabel { Text = name, Foreground = Palette.Ink, FontSize = 12 }, 180, 22));
                var plainLabel = Pixels(OffscreenPreview.Render(new TextBlock { Text = name, FontFamily = Palette.BrandFont, Foreground = Palette.Ink, FontSize = 12 }, 180, 22));
                Check(Enumerable.Range(0, 180 * 22).Count(i => protectedLabel[i * 4 + 3] > 40 && plainLabel[i * 4 + 3] < 5) > 30, "letter-shaped contrast protection surrounds text without boxing its row");
                var field = new ParticleField(71);
                var rest = Pixels(OffscreenPreview.Render(field, 236, 60)); field.SetActive(true, false);
                var settled = Pixels(OffscreenPreview.Render(field, 236, 60));
                double Mean(int left, int top, int width, int height) => Enumerable.Range(left, width).Sum(x => Enumerable.Range(top, height).Sum(y => (double)settled[(y * 236 + x) * 4 + 3])) / (width * height);
                Check(Mean(60, 20, 130, 20) > Mean(60, 4, 130, 7) * 1.4 && Mean(60, 20, 130, 20) > Mean(60, 50, 130, 7) * 1.4,
                    "grain disperses outward from the row interior without a denser geometric outline");
                int Visible(byte[] bytes) => Enumerable.Range(0, bytes.Length / 4).Count(i => bytes[i * 4 + 3] >= 16);
                Check(Visible(settled) > Visible(rest) * 2, "settled selection retains a visible, denser particle surface rather than fading to a few dots");
                field.Stop();
                Check(!field.MotionRunning && settled.SequenceEqual(Pixels(OffscreenPreview.Render(field, 236, 60))), "settled particles remain still and stopping preserves their static appearance");
                for (int i = 0; i < 8; i++)
                {
                    var p = SpatialWheel.LabelPosition(i);
                    Check(SpatialWheel.Hit(AppearanceLayout.Typefield, p.X - 210 - 33, p.Y - 210 + 15) == i && SpatialWheel.Hit(AppearanceLayout.Typefield, p.X - 210 + 33, p.Y - 210 + 15) == i, "particle wheel retains the entire visible command label inside its real hit region: " + i);
                }
                Check(SpatialWheel.Hit(AppearanceLayout.Typefield, 0, 0) == -1, "particle wheel keeps its cancel center clear");
            }
        }
        Check(BackdropTone.IsDark(Enumerable.Repeat(Colors.Black, 30).Concat(Enumerable.Repeat(Colors.White, 6))) &&
            !BackdropTone.IsDark(Enumerable.Repeat(Colors.White, 30).Concat(Enumerable.Repeat(Colors.Black, 6))), "background classification ignores a minority of bright text or dark marks");
        Check(!BackdropTone.IsDark([]), "an unavailable sample has a safe light fallback");
        using (var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Particle lifecycle fixture") { ParentWindow = new nint(-3), Width = 236, Height = 60 }))
        {
            var particles = new ParticleField(42); host.RootVisual = particles;
            OffscreenPreview.Render(particles, 236, 60); particles.SetActive(true); particles.Pulse();
            Check(particles.MotionRunning == (SystemParameters.ClientAreaAnimation && particles.IsLoaded && particles.IsVisible), "particle pulse respects visibility and the system motion preference");
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
            OffscreenPreview.Render(particles, 236, 60);
            Check(!particles.MotionRunning, "particle clock finishes after its short pulse without a permanent render loop");
            particles.Pulse(); particles.Stop();
            Check(!particles.MotionRunning, "hiding can cancel a fresh pulse immediately");
            host.RootVisual = null;
        }
        Palette.SetBackdropDark(false);
        var symbol = new UiIcon { Kind = "settings", Width = 24, Height = 24 };
        var entry = new EntryIcon { Entry = new DesktopEntry("quality", "设置", "", "command", "settings", ""), Width = 24, Height = 24 };
        Palette.SetBackdropDark(true); var symbolBefore = Pixels(OffscreenPreview.Render(symbol, 24, 24)); var entryBefore = Pixels(OffscreenPreview.Render(entry, 24, 24));
        Palette.SetBackdropDark(false);
        Check(!symbolBefore.SequenceEqual(Pixels(OffscreenPreview.Render(symbol, 24, 24))), "unloaded symbol follows adaptive tone changes");
        Check(!entryBefore.SequenceEqual(Pixels(OffscreenPreview.Render(entry, 24, 24))), "preloaded command icon follows adaptive tone changes");
        var particleIcon = new ParticleIcon("settings"); var iconLight = Pixels(OffscreenPreview.Render(particleIcon, 24, 24));
        Palette.SetBackdropDark(true);
        Check(!iconLight.SequenceEqual(Pixels(OffscreenPreview.Render(particleIcon, 24, 24))), "particle menu and wheel symbols follow adaptive tone changes while unloaded");
        Palette.SetBackdropDark(false);
        Palette.Apply(original);
        Console.WriteLine($"PASS: {count} appearance quality checks (black/white contrast, long labels, hidden icon updates)");
    }
}
