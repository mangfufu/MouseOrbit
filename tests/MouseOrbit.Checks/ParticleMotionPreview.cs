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
            using (var host = new HwndSource(new HwndSourceParameters("Particle motion preview") { ParentWindow = new nint(-3), Width = 452, Height = 400 }))
            using (var gif = new AnimatedGifWriter(File.Create(Path.Combine(output, "particle-motion-" + (dark ? "dark" : "light") + ".gif"))))
            {
                host.RootVisual = scene; OffscreenPreview.Render(scene, 452, 400);
                var view = quick.SpatialView!; view.RevealParticles();
                var time = Stopwatch.StartNew(); int stage = 0;
                for (int frame = 0; frame < 152; frame++)
                {
                    int wait = (int)(frame * 50 - time.ElapsedMilliseconds); if (wait > 0) ParticleMotionChecks.Advance(wait);
                    var buttons = ((StackPanel)view.Flow.Content).Children.OfType<Button>().Where(b => b.Tag is string).ToArray();
                    if (frame >= 22 && stage == 0) { view.FocusRow(buttons[Math.Min(2, buttons.Length - 1)]); stage++; }
                    if (frame >= 44 && stage == 1) { view.FocusRow(buttons[Math.Min(3, buttons.Length - 1)]); stage++; }
                    if (frame >= 64 && stage == 2)
                    { buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); stage++; }
                    if (frame >= 86 && stage == 3) { view.Collapse(); stage++; }
                    if (frame >= 102 && stage == 4)
                    {
                        view.FocusRow(buttons[Math.Min(2, buttons.Length - 1)]); view.HoldMenu(true);
                        Canvas.SetTop(wing, 12 + view.MenuAnchor.TransformToAncestor(panel).Transform(new Point()).Y - 32);
                        wing.Visibility = Visibility.Visible; Ui.Reveal(wing);
                        int index = 0; foreach (var item in menuItems) Ui.Reveal(item, index++ * 12); stage++;
                    }
                    if (frame >= 122 && stage == 5) { wing.Visibility = Visibility.Hidden; view.HoldMenu(false); stage++; }
                    if (frame >= 130 && stage == 6) { view.SelectCategory("recent"); stage++; }
                    if (frame >= 144 && stage == 7) { view.StopMotion(); panel.Visibility = Visibility.Hidden; stage++; }
                    var image = OffscreenPreview.Render(scene, 452, 400); gif.Add(image, 50);
                    if (frame == 114)
                    {
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                        using var stream = File.Create(Path.Combine(output, "particle-motion-menu-" + (dark ? "dark" : "light") + ".png")); png.Save(stream);
                    }
                }
                view.StopMotion(); host.RootVisual = null;
            }
            quick.Close();
        }
        Console.WriteLine("PASS: real particle animation GIFs exported from message-only native controls (light/dark; no foreground input)");
    }
}
