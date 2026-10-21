using MouseOrbit;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static class UiPerformanceChecks
{
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    internal static void Run()
    {
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Console.WriteLine($"PERF: WPF tier {RenderCapability.Tier >> 16}, render mode {RenderOptions.ProcessRenderMode}");
        var operations = new Dictionary<DispatcherOperation, long>();
        Dispatcher.CurrentDispatcher.Hooks.OperationStarted += (_, args) => operations[args.Operation] = Stopwatch.GetTimestamp();
        Dispatcher.CurrentDispatcher.Hooks.OperationCompleted += (_, args) =>
        {
            if (!operations.Remove(args.Operation, out long start)) return;
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (elapsed < 25) return;
            var callback = typeof(DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(args.Operation) as Delegate;
            Console.WriteLine($"PERF: dispatcher operation {args.Operation.Priority}, {callback?.Method.DeclaringType?.Name}.{callback?.Method.Name}: {elapsed:F2} ms");
        };
        Palette.Apply("pixel-light");
        var panel = new QuickPanelWindow();
        var entries = Enumerable.Range(0, 40).Select(i => new DesktopEntry("perf:" + i, "测试项目 " + i, "性能测试", "command", "downloads", "↓")).ToArray();
        panel.SetWindows(Enumerable.Range(0, 12).Select(i => new SwitchGroup("app:" + i, "测试应用 " + i,
            new[] { new SwitchWindow(0, 0, "app:" + i, "应用", "窗口", false, 1, i) { Preview = RedesignChecks.Solid(160, 90, Colors.Gray) } })).ToArray());
        panel.Open(new Native.Point { X = 400, Y = 300 }, new() { ["frequent"] = entries[..24], ["recent"] = entries }); Pump();
        try
        {
            var samples = new List<double>();
            foreach (string name in Enumerable.Range(0, 60).Select(i => new[] { "常用", "最近", "窗口" }[i % 3]))
            {
                var watch = Stopwatch.StartNew(); panel.SelectCategory(name == "窗口" ? "windows" : name == "常用" ? "frequent" : "recent"); double action = watch.Elapsed.TotalMilliseconds;
                panel.UpdateLayout(); double layout = watch.Elapsed.TotalMilliseconds; Pump(); watch.Stop();
                samples.Add(watch.Elapsed.TotalMilliseconds);
                if (samples.Count <= 3) Console.WriteLine($"PERF: panel FIRST {name}: {watch.Elapsed.TotalMilliseconds:F2} ms (handler {action:F2}, layout {layout - action:F2})");
                else if (watch.Elapsed.TotalMilliseconds > 16) Console.WriteLine($"PERF: panel slow {name}: {watch.Elapsed.TotalMilliseconds:F2} ms (handler {action:F2}, layout {layout - action:F2}, dispatcher {watch.Elapsed.TotalMilliseconds - layout:F2})");
            }
            var ordered = samples.OrderBy(value => value).ToArray();
            Console.WriteLine($"PERF: panel 60 switches: p50 {ordered[30]:F2} ms, p95 {ordered[56]:F2} ms, max {ordered[^1]:F2} ms");
            var changed = new List<double>();
            var openCalls = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                panel.Hide();
                var next = entries[..24].Prepend(new DesktopEntry("new:" + i, "新增应用 " + i, "", "command", "downloads", "")).ToArray();
                var watch = Stopwatch.StartNew(); panel.Open(new Native.Point { X = 400, Y = 300 }, new() { ["frequent"] = next, ["recent"] = entries }); openCalls.Add(watch.Elapsed.TotalMilliseconds); panel.UpdateLayout(); Pump(); watch.Stop(); changed.Add(watch.Elapsed.TotalMilliseconds);
                if (openCalls[^1] > 20) Console.WriteLine($"PERF: changed reopen detail (data, screen, layout, show): {panel.LastOpenTimings}");
            }
            Console.WriteLine($"PERF: 20 side-button reopens after changed content: open handler max {openCalls.Max():F2} ms; complete p50 {changed.OrderBy(value => value).ElementAt(10):F2} ms, p95 {changed.OrderBy(value => value).ElementAt(18):F2} ms, max {changed.Max():F2} ms");
        }
        finally { panel.Close(); }
        foreach (bool legacy in new[] { true, false })
        {
            var manager = new MainWindow(new Preferences());
            try
            {
                var list = (ListBox)manager.FindName("ResultsList");
                if (legacy) list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
                list.ItemsSource = Enumerable.Range(0, 800).Select(i => new DesktopEntry("perf-app:" + i, "性能应用 " + i, "", "command", "downloads", "↓")).ToArray();
                var watch = Stopwatch.StartNew(); OffscreenPreview.Render((FrameworkElement)manager.Content, 1040, 740); watch.Stop();
                int realized = Enumerable.Range(0, 800).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null);
                Console.WriteLine($"PERF: manager 800 apps, {(legacy ? "legacy wrap" : "virtualized")}: {watch.Elapsed.TotalMilliseconds:F2} ms, containers {realized}");
            }
            finally { manager.Close(); }
        }
        var cachedManager = new MainWindow(new Preferences()) { ShowActivated = false };
        try
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var launcher = (Launcher)typeof(MainWindow).GetField("_launcher", flags)!.GetValue(cachedManager)!;
            ((List<DesktopEntry>)launcher.Entries).AddRange(Enumerable.Range(0, 800).Select(i => new DesktopEntry("cached-app:" + i, "应用 " + i, "", "app", "unused", "")));
            typeof(MainWindow).GetField("_lastIndexRefresh", flags)!.SetValue(cachedManager, Environment.TickCount64);
            var prepare = Stopwatch.StartNew(); cachedManager.PrepareView(); prepare.Stop(); cachedManager.Show(); Pump();
            Console.WriteLine($"PERF: manager startup preparation (800 apps, all pages): {prepare.Elapsed.TotalMilliseconds:F2} ms");
            var samples = new List<double>();
            foreach (string key in Enumerable.Range(0, 40).Select(i => new[] { "apps", "recent", "windows", "system", "frequent" }[i % 5]))
            {
                var tab = Descendants<Button>(cachedManager).Single(b => b.Tag as string == key);
                var watch = Stopwatch.StartNew(); tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); cachedManager.UpdateLayout(); Pump(); watch.Stop();
                samples.Add(watch.Elapsed.TotalMilliseconds);
                if (samples.Count <= 5) Console.WriteLine($"PERF: manager FIRST {key}: {watch.Elapsed.TotalMilliseconds:F2} ms");
            }
            Console.WriteLine($"PERF: manager 40 switches: p95 {samples.OrderBy(value => value).ElementAt(37):F2} ms, max {samples.Max():F2} ms");
        }
        finally { cachedManager.Close(); }
        var fixture = new Window { Title = "MouseOrbit performance fixture", Width = 1000, Height = 700, ShowActivated = false };
        fixture.Show(); Pump();
        var nativePanel = new QuickPanelWindow();
        try
        {
            var source = new WindowInteropHelper(fixture).Handle;
            nativePanel.SetWindows(Enumerable.Range(0, 12).Select(i => new SwitchGroup("native:" + i, "实时窗口 " + i,
                new[] { new SwitchWindow(source, (uint)Environment.ProcessId, "native:" + i, "应用", "实时窗口", false, 1, i) })).ToArray());
            nativePanel.Open(new Native.Point { X = 400, Y = 300 }, new() { ["frequent"] = entries[..24], ["recent"] = entries }); Pump();
            var samples = new List<double>(); var work = new List<double>();
            foreach (string name in Enumerable.Range(0, 30).Select(i => new[] { "常用", "最近", "窗口" }[i % 3]))
            {
                var watch = Stopwatch.StartNew(); nativePanel.SelectCategory(name == "窗口" ? "windows" : name == "常用" ? "frequent" : "recent"); nativePanel.UpdateLayout(); work.Add(watch.Elapsed.TotalMilliseconds); Pump(); watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"PERF: rail with 12 native window icons, 30 switches: handler+layout max {work.Max():F2} ms; dispatcher p95 {samples.OrderBy(value => value).ElementAt(28):F2} ms, max {samples.Max():F2} ms");
        }
        finally { nativePanel.Close(); }
        using var trail = new PointerTrail(); var hint = new GestureHint();
        try
        {
            var target = WindowTarget.Capture(new WindowInteropHelper(fixture).Handle);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                trail.Path.Begin(new(200, 200));
                var watch = Stopwatch.StartNew();
                double max = 0, total = 0;
                for (int i = 1; i <= 12; i++)
                {
                    var update = Stopwatch.StartNew();
                    trail.Path.Add(new(200 + i * 10, 200 + i * 6)); trail.Refresh(); hint.Update(new Native.Point { X = 200 + i * 10, Y = 200 + i * 6 }, target, "左半屏");
                    update.Stop(); total += update.Elapsed.TotalMilliseconds; max = Math.Max(max, update.Elapsed.TotalMilliseconds); Pump();
                }
                watch.Stop(); Console.WriteLine($"PERF: gesture 12 updates, cycle {cycle}: {watch.Elapsed.TotalMilliseconds:F2} ms, draw mean {total / 12:F2} ms, max {max:F2} ms");
                trail.Clear(); hint.Clear(); Pump();
            }
        }
        finally { hint.Dispose(); fixture.Close(); }
        using (var renderer = new GestureRenderer())
        {
            renderer.Ready.GetAwaiter().GetResult(); var path = new PointerPath(); path.Begin(new(200, 200));
            var samples = new List<double>();
            for (int i = 1; i <= 120; i++)
            {
                path.Add(new(200 + i * 3, 200 + i));
                var watch = Stopwatch.StartNew(); renderer.Update(path.Points, new Native.Point { X = 200 + i * 3, Y = 200 + i }, default, "继续划动", true); watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds); Pump();
            }
            renderer.Clear(); renderer.ProbeAsync().GetAwaiter().GetResult();
            if (renderer.Failures != 0) throw new Exception("Graphics-thread rendering failed");
            Console.WriteLine($"PERF: production gesture submission 120 updates: p95 {samples.OrderBy(value => value).ElementAt(113):F3} ms, max {samples.Max():F3} ms; rendered {renderer.RenderedFrames} latest frames");
        }
        var gesture = new StrokeGesture(100, 0);
        for (int i = 1; i < 2000; i++) { double a = i * Math.PI * 2 / 1999; gesture.Add(Math.Cos(a) * 100, Math.Sin(a) * 100, i); }
        var recognition = Stopwatch.StartNew(); for (int i = 0; i < 500; i++) { gesture.TryEnterVolume(); gesture.Recognize(); } recognition.Stop();
        Console.WriteLine($"PERF: recognize long stroke: {recognition.Elapsed.TotalMilliseconds / 500:F3} ms/update");
    }
}
