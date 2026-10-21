using MouseOrbit;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Live native animation clocks in a message-only host. Never shows or clicks a desktop window.
internal static class ParticleMotionPreview
{
    internal static void Write(IReadOnlyList<SwitchGroup> groups, IReadOnlyList<DesktopEntry> apps, string output)
    {
        foreach (bool dark in new[] { false, true })
        {
            Palette.SetBackdropDark(dark);
            var quick = new QuickPanelWindow(); quick.SetWindows(groups);
            quick.SetEntries(new() { ["recent"] = apps, ["frequent"] = apps });
            var panel = (FrameworkElement)quick.Content; quick.Content = null;
            panel.Width = 254; panel.Height = 376;
            var scene = new Canvas { Width = 452, Height = 400,
                Background = new SolidColorBrush(dark ? Color.FromRgb(24, 26, 29) : Color.FromRgb(250, 250, 248)) };
            Palette.Resources(scene.Resources);
            scene.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MouseOrbit;component/Controls.xaml", UriKind.Relative) });
            scene.Children.Add(panel); Canvas.SetLeft(panel, 4); Canvas.SetTop(panel, 12);
            var menu = quick.CreateMenu(); var menuItems = menu.Items.OfType<MenuItem>().ToArray(); menu.Items.Clear();
            var items = new StackPanel(); foreach (var item in menuItems) items.Children.Add(item);
            var wing = new ParticleMenuPlate { Child = items, Padding = new Thickness(10), Width = 178, Visibility = Visibility.Hidden };
            scene.Children.Add(wing); Canvas.SetLeft(wing, 248);
            var pointer = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L0,16 L4,12 L7,19 L10,18 L7,11 L13,11 Z"),
                Fill = Palette.Ink, Stroke = Palette.Surface, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Hidden };
            scene.Children.Add(pointer);
            string frames = Path.Combine(output, "liquid-frames-dark"); if (dark) Directory.CreateDirectory(frames);
            using (var host = new HwndSource(new HwndSourceParameters("Particle motion preview") { ParentWindow = new nint(-3), Width = 452, Height = 400 }))
            using (var gif = new AnimatedGifWriter(File.Create(Path.Combine(output, "particle-motion-" + (dark ? "dark" : "light") + ".gif"))))
            using (var idle = new AnimatedGifWriter(File.Create(Path.Combine(output, "particle-idle-" + (dark ? "dark" : "light") + ".gif"))))
            {
                host.RootVisual = scene; OffscreenPreview.Render(scene, 452, 400);
                var view = quick.SpatialView!; view.RevealParticles();
                var time = Stopwatch.StartNew(); int stage = 0;
                FlowClock.CaptureMode = true;
                try
                {
                for (int frame = 0; frame < 636; frame++)
                {
                    int wait = (int)(frame * 1000d / 60 - time.ElapsedMilliseconds); if (wait > 0) ParticleMotionChecks.Advance(wait);
                    var buttons = ((StackPanel)view.Flow.Content).Children.OfType<Button>().Where(b => b.Tag is string).ToArray();
                    if (frame >= 82 * 3 && stage == 0) { view.FocusRow(buttons[Math.Min(2, buttons.Length - 1)]); stage++; }
                    if (frame >= 104 * 3 && stage == 1) { view.FocusRow(buttons[Math.Min(3, buttons.Length - 1)]); stage++; }
                    if (frame >= 124 * 3 && stage == 2)
                    { buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); stage++; }
                    if (frame >= 146 * 3 && stage == 3) { view.Collapse(); stage++; }
                    if (frame >= 162 * 3 && stage == 4)
                    {
                        view.FocusRow(buttons[Math.Min(2, buttons.Length - 1)]); view.HoldMenu(true);
                        Canvas.SetTop(wing, 12 + view.MenuAnchor.TransformToAncestor(panel).Transform(new Point()).Y - 32);
                        wing.Visibility = Visibility.Visible; Ui.Reveal(wing);
                        int index = 0; foreach (var item in menuItems) Ui.Reveal(item, index++ * 12); stage++;
                    }
                    if (frame >= 182 * 3 && stage == 5) { wing.Visibility = Visibility.Hidden; view.HoldMenu(false); stage++; }
                    if (frame >= 190 * 3 && stage == 6) { view.SelectCategory("recent"); stage++; }
                    if (frame >= 204 * 3 && stage == 7) { view.StopMotion(); panel.Visibility = Visibility.Hidden; stage++; }
                    if (frame is >= 255 and < 366)
                    {
                        var location = new Point(72 + (frame - 255) / 111d * 134, 150 + Math.Sin((frame - 255) / 22d) * 9);
                        view.FeedPointer(location); pointer.Visibility = Visibility.Visible;
                        Canvas.SetLeft(pointer, location.X + 4); Canvas.SetTop(pointer, location.Y + 12);
                    }
                    else { view.FeedPointer(null); pointer.Visibility = Visibility.Hidden; }
                    FlowClock.CaptureStep(1d / 60);
                    var image = OffscreenPreview.Render(scene, 452, 400);
                    if (frame % 3 == 0) { gif.Add(image, 50); if (frame < 240) idle.Add(image, 50); }
                    if (dark)
                    {
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using var stream = File.Create(Path.Combine(frames, $"frame-{frame:0000}.png")); encoder.Save(stream);
                    }
                    if (frame == 174 * 3)
                    {
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                        using var stream = File.Create(Path.Combine(output, "particle-motion-menu-" + (dark ? "dark" : "light") + ".png")); png.Save(stream);
                    }
                }
                }
                finally { FlowClock.CaptureMode = false; view.StopMotion(); host.RootVisual = null; }
            }
            quick.Close();
        }
        Console.WriteLine("PASS: real liquid UI exports (60 Hz spring model, pointer deflection, light/dark GIFs; no foreground input)");
    }
}
