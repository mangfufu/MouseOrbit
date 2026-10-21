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
            bool motion = SystemParameters.ClientAreaAnimation;
            Check(field.AmbientRunning == motion, "presentation requested before Loaded starts automatically when the field loads");
            field.SetPresented(true);
            Check(field.AmbientRunning == motion, "explicit presentation starts slow dust only with system motion enabled");
            var before = Pixels(OffscreenPreview.Render(field, 236, 60)); Advance(350);
            var after = Pixels(OffscreenPreview.Render(field, 236, 60));
            Check(!motion || !before.SequenceEqual(after), "visible slow dust actually changes native rendered pixels");
            Check(field.MotionRunning == motion, "ambient flow remains after the finite entrance has finished");
            Advance(1600); before = Pixels(OffscreenPreview.Render(field, 236, 60)); Advance(500);
            var idle = Pixels(OffscreenPreview.Render(field, 236, 60));
            Check(!motion || Enumerable.Range(0, idle.Length / 4).Count(i => Math.Abs(before[i * 4 + 3] - idle[i * 4 + 3]) >= 12) >= 64,
                "idle flow changes a visible area, rather than only a few subpixel grains, without any pointer events");
            Check(field.RenderTransform.Value.IsIdentity && field.RenderSize == new Size(236, 60) && !field.IsHitTestVisible,
                "ambient particles leave row bounds and the input plane stable");
            field.SetActive(true); Advance(45); field.SetActive(false); Advance(45); field.SetActive(true); field.SetJoined(true); Advance(220);
            Check(field.AmbientRunning == motion, "rapid hover and menu retargeting do not restart or cancel ambient flow");
            field.Stop(); before = Pixels(OffscreenPreview.Render(field, 236, 60)); Advance(120);
            Check(!field.MotionRunning && before.SequenceEqual(Pixels(OffscreenPreview.Render(field, 236, 60))), "closing removes every clock and leaves a stable frame immediately");
            Check(Math.Abs(field.FlowState.Focus.Value - 1) < .001 && Math.Abs(field.FlowState.Join.Value - 1) < .001,
                "interrupted selection converges to its joined state while preserving the current flow phase");
            field.SetPresented(true); field.Visibility = Visibility.Hidden; Advance(10);
            Check(!field.MotionRunning, "a hidden material cannot keep running");
            field.Visibility = Visibility.Visible; host.RootVisual = null;
            var canceled = new ParticleField(12); canceled.SetPresented(true); canceled.Stop(); host.RootVisual = canceled;
            OffscreenPreview.Render(canceled, 236, 60);
            Check(!canceled.MotionRunning, "closing before Loaded cancels the pending presentation request"); host.RootVisual = null;

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
            view.SetData(groups, new());
            view.Measure(new Size(254, 376)); view.Arrange(new Rect(0, 0, 254, 376)); view.UpdateLayout();
            view.RevealParticles(); OffscreenPreview.Render(view, 254, 376); Advance(50);
            Check(!motion || Descendants<ParticleField>(view).Any(f => f.AmbientRunning), "fresh rows start ambient flow when Open precedes their Loaded event");
            view.RevealParticles(); Advance(50); host.RootVisual = null; Advance(30);
            Check(Descendants<ParticleField>(view).All(f => !f.MotionRunning), "unloading removes all ambient clocks without a visible test window");
            var wheel = new OrbitVisual(); wheel.SetPresented(true); host.RootVisual = wheel;
            OffscreenPreview.Render(wheel, 420, 420);
            var wheelFields = Descendants<ParticleField>(wheel).ToArray();
            Check(wheelFields.Length == 8 && (!motion || wheelFields.All(f => f.AmbientRunning)), "hold wheel uses the same eight independently flowing particle options");
            wheel.SetPresented(false);
            Check(wheelFields.All(f => !f.MotionRunning), "releasing the wheel stops all of its material clocks"); host.RootVisual = null;
            groups[0] = groups[0] with { Windows = new[] { groups[0].Recent, groups[0].Recent with { Title = "第二个窗口" } } };
            view = new SpatialSwitcherView { Width = 254, Height = 376 }; view.SetData(groups, new()); host.RootVisual = view;
            OffscreenPreview.Render(view, 254, 376); view.RevealParticles();
            var stack = (StackPanel)view.Flow.Content; var parent = stack.Children.OfType<Button>().First();
            var parentPosition = parent.TransformToAncestor(view).Transform(new Point());
            parent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); OffscreenPreview.Render(view, 254, 376); Advance(100);
            var childRows = stack.Children.OfType<Button>().Where(b => b.Tag is SwitchWindow).ToArray();
            Check(childRows.Length == 2 && childRows.All(b => b.Height is >= 0 and < 44) && parentPosition == parent.TransformToAncestor(view).Transform(new Point()),
                "elastic expansion changes real child geometry while preserving its parent target");
            parent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Advance(45);
            double partialHeight = childRows[0].Height;
            parent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!motion || (stack.Children.OfType<Button>().Where(b => b.Tag is SwitchWindow).SequenceEqual(childRows) && Math.Abs(childRows[0].Height - partialHeight) < .01),
                "reversing a half-closed group reuses its windows and continues without a size jump");
            Advance(650); OffscreenPreview.Render(view, 254, 376);
            Check(childRows.All(b => Math.Abs(b.Height - 40) < .1), "elastic rows settle at their exact clickable size");
            view.StopMotion(); host.RootVisual = null; Advance(40);
            Check(FlowClock.ActiveCount == 0, "hiding removes the shared fluid, scroll and expansion frame clients");
        }
        foreach (double dpi in new[] { 1d, 1.5, 2d, 3d })
        {
            var area = new Rect(-1920, -200, 1920, 1080);
            var placement = WindowPreviewWindow.Place(new Rect(-240, 750, 220, 60), area, dpi);
            Check(area.Contains(placement) && placement.Right < -240, "window preview flips to the left and stays inside the work area at DPI " + dpi);
        }
        var portrait = LiveThumbnail.FitBounds(new Rect(0, 0, 280, 160), new Size(900, 1600));
        Check(portrait == new Rect(95, 0, 90, 160), "portrait previews fit the entire source instead of cropping its content");
        Check(new Rect(0, 0, 120, 90).Contains(WindowPreviewWindow.Place(new Rect(20, 30, 80, 40), new Rect(0, 0, 120, 90), 2)), "preview remains inside a work area smaller than its preferred size");
        Console.WriteLine($"PASS: {count} particle motion checks (live pixels, stable targets, interrupted transitions, visible-only clocks)");
    }
}
