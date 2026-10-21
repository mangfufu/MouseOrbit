using MouseOrbit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class ParticleMotionChecks
{
    internal static void Advance(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds)) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static byte[] Pixels(BitmapSource bitmap)
    { var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes; }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    internal static void Run()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); count++; }
        var field = new ParticleField(42); field.SetPresented(true);
        Check(!field.MotionRunning, "unloaded or preloaded material cannot start a background loop");
        using (var host = new HwndSource(new HwndSourceParameters("Particle motion fixture") { ParentWindow = new nint(-3), Width = 254, Height = 376 }))
        {
            host.RootVisual = field; OffscreenPreview.Render(field, 236, 60);
            field.SetPresented(true);
            bool motion = SystemParameters.ClientAreaAnimation;
            Check(field.AmbientRunning == motion, "explicit presentation starts slow dust only with system motion enabled");
            var before = Pixels(OffscreenPreview.Render(field, 236, 60)); Advance(350);
            var after = Pixels(OffscreenPreview.Render(field, 236, 60));
            Check(!motion || !before.SequenceEqual(after), "visible slow dust actually changes native rendered pixels");
            Check(field.MotionRunning == motion, "ambient flow remains after the finite entrance has finished");
            Check(field.RenderTransform.Value.IsIdentity && field.RenderSize == new Size(236, 60) && !field.IsHitTestVisible,
                "ambient particles leave row bounds and the input plane stable");
            field.SetActive(true); Advance(45); field.SetActive(false); Advance(45); field.SetActive(true); field.SetJoined(true); Advance(220);
            Check(field.AmbientRunning == motion, "rapid hover and menu retargeting do not restart or cancel ambient flow");
            field.Stop(); before = Pixels(OffscreenPreview.Render(field, 236, 60)); Advance(120);
            Check(!field.MotionRunning && before.SequenceEqual(Pixels(OffscreenPreview.Render(field, 236, 60))), "closing removes every clock and leaves a stable frame immediately");
            var reference = new ParticleField(42); reference.SetActive(true, false); reference.SetJoined(true);
            Check(before.SequenceEqual(Pixels(OffscreenPreview.Render(reference, 236, 60))), "interrupted selection converges to the correct joined material");
            field.SetPresented(true); field.Visibility = Visibility.Hidden; Advance(10);
            Check(!field.MotionRunning, "a hidden material cannot keep running");
            field.Visibility = Visibility.Visible; host.RootVisual = null;

            var view = new SpatialSwitcherView { Width = 254, Height = 376 };
            var groups = Enumerable.Range(0, 18).Select(i => new SwitchGroup("motion-" + i, "应用 " + i,
                new[] { new SwitchWindow(0, 0, "motion-" + i, "应用 " + i, "窗口 " + i, false, 1, 100 - i) })).ToArray();
            view.SetData(groups, new()); host.RootVisual = view; OffscreenPreview.Render(view, 254, 376);
            var fields = Descendants<ParticleField>(view).ToArray();
            Check(fields.Length == 18 && fields.All(f => !f.AmbientRunning), "prepared native view has no ambient activity before actual presentation");
            var firstButton = ((StackPanel)view.Flow.Content).Children.OfType<Button>().First();
            var position = firstButton.TransformToAncestor(view).Transform(new Point());
            view.RevealParticles(); Advance(300); OffscreenPreview.Render(view, 254, 376);
            Check(!motion || (fields.Count(f => f.AmbientRunning) is > 0 and <= 7 && fields[^1].AmbientRunning == false), "only viewport-intersecting rows animate");
            Check(firstButton.TransformToAncestor(view).Transform(new Point()) == position && firstButton.RenderTransform.Value.IsIdentity,
                "staggered appearance never translates an app button or changes its click target");
            view.Flow.Jump(8 * SpatialSwitcherView.RowHeight); OffscreenPreview.Render(view, 254, 376);
            Check(!fields[0].AmbientRunning && (!motion || fields[8].AmbientRunning), "scrolling pauses the outgoing row and resumes newly visible dust");
            view.StopMotion(); Advance(50);
            Check(fields.All(f => !f.MotionRunning), "hiding a prepared view immediately stops offscreen and visible fields alike");
            view.SetData(groups, new()); OffscreenPreview.Render(view, 254, 376);
            Check(Descendants<ParticleField>(view).All(f => !f.MotionRunning), "a deferred rebuild cannot restart a hidden overlay");
            view.RevealParticles(); Advance(50); host.RootVisual = null; Advance(30);
            Check(Descendants<ParticleField>(view).All(f => !f.MotionRunning), "unloading removes all ambient clocks without a visible test window");
        }
        Console.WriteLine($"PASS: {count} particle motion checks (live pixels, stable targets, interrupted transitions, visible-only clocks)");
    }
}
