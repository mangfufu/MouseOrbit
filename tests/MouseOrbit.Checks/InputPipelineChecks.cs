using MouseOrbit;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

internal static class InputPipelineChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        int wakeups = 0, nativeQueries = 0;
        var bridge = new MouseInputBridge(() => wakeups++, new Native.Point(), () => { nativeQueries++; return 42; }, _ => { nativeQueries++; return 42; }, _ => false);
        var normal = new MouseInputContext(true, "XButton2", true, false, false); bridge.Publish(normal);
        Native.MouseData Data(int x = 100, int y = 100, uint button = 0) => new() { Pt = new() { X = x, Y = y }, Mouse = button << 16 };
        var watch = Stopwatch.StartNew();
        bool movementConsumed = false;
        for (int i = 0; i < 100_000; i++) movementConsumed |= bridge.Process(0x200, Data(-i, i), i);
        watch.Stop();
        Check(!movementConsumed, "ordinary movement is never suppressed");
        Check(wakeups == 0 && nativeQueries == 0 && !bridge.HasPending, "100000 ordinary moves do not wake UI, enumerate windows, or inspect processes");
        Check(bridge.Latest.X == -99999 && bridge.Latest.Y == 99999, "atomic pointer snapshot preserves negative multi-monitor coordinates");
        Console.WriteLine($"INPUT: 100000 idle moves in {watch.Elapsed.TotalMilliseconds:F1} ms; UI wakeups: {wakeups}");

        Check(!bridge.Process(0x20B, Data(button: 1), 1000), "unbound back-button press passes through");
        Check(bridge.Process(0x20B, Data(button: 2), 1000), "bound forward-button press is captured");
        Check(!bridge.Process(0x20C, Data(button: 1), 1010), "other side-button release cannot end captured press");
        Check(bridge.Process(0x20C, Data(button: 2), 1100), "bound side-button release is captured");
        var samples = bridge.Drain(out bool overflowed);
        Check(!overflowed && samples.Count == 2 && samples[0].Start == MouseStart.Trigger && samples[0].Target == 42, "tap retains ordered down/up and original target");
        var press = new TriggerPress(); press.Begin(samples[0].Time, false);
        Check(press.End(samples[1].Time, 0, 0) == TriggerRelease.Panel, "a queued 100ms tap remains a tap even if UI resumes much later");

        wakeups = 0;
        Check(bridge.Process(0x204, Data(), 2000), "right gesture captures down");
        for (int i = 1; i <= 8000; i++) CheckMove(bridge.Process(0x200, Data(i / 8, i / 16), 2000 + i / 8));
        Check(bridge.Process(0x205, Data(1000, 500), 3001), "gesture release is captured after a high-polling burst");
        samples = bridge.Drain(out overflowed);
        Check(!overflowed && samples.Count <= 252 && wakeups == 1, "8000Hz movement produces at most 250 motion samples and one UI wakeup per blocked burst");
        Check(samples[0].Start == MouseStart.Gesture && samples[^1].Message == 0x205 && samples[^1].Data.Pt.X == 1000, "gesture start and final endpoint are never coalesced away");

        bridge.Process(0x204, Data(), 4000);
        for (int i = 1; i <= 2000; i++) bridge.Process(0x200, Data(i, 0), 4000 + i * 4);
        bridge.Process(0x205, Data(2000, 0), 12001);
        samples = bridge.Drain(out overflowed);
        Check(overflowed && samples.Count <= MouseInputBridge.MaximumPendingMoves + 2, "a long UI stall cannot grow the gesture queue without bound");
        Check(samples[^1].Message == 0x205, "overflow preserves release so the pointer cannot remain captured");
        bridge.Process(0x204, Data(), 13000);
        bridge.Publish(normal with { Enabled = false });
        Check(bridge.Process(0x205, Data(), 13020), "pausing during a held gesture still consumes its matching release");
        Check(!bridge.Process(0x204, Data(), 13030), "new clicks pass through while paused"); bridge.Drain(out _);

        var own = new MouseInputBridge(() => { }, new(), () => 42, _ => 42, _ => true); own.Publish(normal);
        Check(!own.Process(0x204, Data(), 1) && !own.Process(0x205, Data(), 2) && !own.HasPending, "settings-window right clicks are not captured as global gestures");
        bridge.Publish(normal with { QuickOpen = true });
        Check(bridge.Process(0x20A, Data(button: 120), 13990), "icon rail scrolling is captured even while the pointer remains next to the rail");
        Check(bridge.Process(0x204, Data(), 14000) && bridge.Process(0x205, Data(), 14020), "right click dismisses an open panel without leaking an unmatched release"); bridge.Drain(out _);
        bridge.Publish(normal);
        Check(!bridge.Process(0x20A, Data(button: 120), 14030), "normal foreground scrolling resumes immediately after the rail closes");
        bridge.Publish(normal with { WheelOpen = true });
        Check(bridge.Process(0x201, Data(), 15000) && bridge.Process(0x202, Data(), 15020), "wheel selection consumes both halves of a left click");
        Check(bridge.Process(0x20A, Data(), 15030), "wheel scrolling cannot leak to the underlying app"); bridge.Drain(out _);
        bridge.Publish(normal);
        bridge.Process(0x20B, Data(button: 2), 16000);
        Check(bridge.Process(0x20B, Data(button: 2), 16001), "driver repeat is suppressed without creating another press");
        bridge.Process(0x20C, Data(button: 2), 16050); samples = bridge.Drain(out _);
        Check(samples.Count == 2, "duplicate down does not leave a second pending action");
        Console.WriteLine($"PASS: {count} input pipeline checks (pass-through, bounded motion, pairing, timestamps)");
        void CheckMove(bool consumed) { if (consumed) throw new Exception("FAIL: captured motion must still pass through to Windows"); }
    }
    internal static void RunHookThread()
    {
        int uiThread = Environment.CurrentManagedThreadId;
        var input = new GlobalInput((_, _) => false);
        try
        {
            if (input.ManagedThreadId == uiThread) throw new Exception("FAIL: hook is running on the UI thread");
            var probe = System.Threading.Tasks.Task.Run(async () => await input.ProbeAsync().WaitAsync(TimeSpan.FromMilliseconds(250)));
            // Deliberately do not pump WPF: the real hook message loop must remain responsive.
            Thread.Sleep(400); probe.GetAwaiter().GetResult();
            if (input.CallbackFailures != 0) throw new Exception("FAIL: exception escaped input handler");
        }
        finally { input.Dispose(); }
        if (input.IsAlive) throw new Exception("FAIL: input thread did not stop cleanly");
        Console.WriteLine("PASS: 4 input-thread integration checks (real hook, UI blocked, clean shutdown)");
    }
    internal static void RunController()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            using var controller = new OrbitController();
            // Exercise the real UI consumer without injecting buttons into Windows or targeting user apps.
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ((GlobalInput)typeof(OrbitController).GetField("_input", flags)!.GetValue(controller)!).Dispose();
            var bridge = (MouseInputBridge)typeof(OrbitController).GetField("_inputBridge", flags)!.GetValue(controller)!;
            controller.Settings.Trigger = "XButton2"; controller.Settings.AutoFocus = controller.Settings.ManualFocus = false;
            bridge.Publish(new(true, "XButton2", true, false, false));
            long now = Environment.TickCount64;
            var tap = System.Threading.Tasks.Task.Run(() =>
            {
                var data = new Native.MouseData { Pt = new() { X = 600, Y = 400 }, Mouse = 2u << 16 };
                bridge.Process(0x20B, data, now); Thread.Sleep(60); bridge.Process(0x20C, data, now + 60);
            });
            Thread.Sleep(350); tap.GetAwaiter().GetResult();
            bool Visible() => Application.Current.Windows.OfType<QuickPanelWindow>().Any(w => w.IsVisible);
            PumpUntil(Visible, 5000);
            if (!Visible()) throw new Exception("FAIL: delayed UI consumer lost a short side-button tap: " + controller.LastStatus);
            controller.Cancel(); PumpFor(30);
            if (Visible()) throw new Exception("FAIL: controller cancellation did not close panel");
            controller.OpenAssistant(false); controller.Cancel(); PumpFor(350);
            if (Visible()) throw new Exception("FAIL: canceled background scan reopened the panel");
            // Two gestures can be drained before either release action is dispatched.
            // The old release must not clear the next gesture's newly collected path.
            var mouse = typeof(OrbitController).GetMethod("OnMouse", flags)!;
            void Send(int message, int x, int y, MouseStart start = MouseStart.None) => mouse.Invoke(controller,
                new object[] { new MouseSample(message, new Native.MouseData { Pt = new() { X = x, Y = y } }, Environment.TickCount64, 0, true, start) });
            controller.Settings.GesturesMap.Clear();
            Send(0x204, 500, 400, MouseStart.Gesture); Send(0x200, 600, 400); Send(0x205, 600, 400);
            Send(0x204, 500, 450, MouseStart.Gesture); Send(0x200, 560, 450); PumpFor(30);
            var path = (PointerPath)typeof(OrbitController).GetField("_path", flags)!.GetValue(controller)!;
            if (path.Points.Count != 2 || path.Points[^1] != new Point(560, 450)) throw new Exception("FAIL: an old release erased the next gesture trajectory");
            controller.Cancel(); PumpFor(30);
            Console.WriteLine("PASS: 4 controller integration checks (delayed tap, cancellation, stale scan, consecutive gestures)");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    internal static void RunGraphicsThread()
    {
        var graphics = new GestureRenderer();
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; }
        try
        {
            graphics.Ready.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Check(graphics.ManagedThreadId != Environment.CurrentManagedThreadId, "graphics runs on a dedicated thread");
            var points = new[] { new Point(180, 180), new Point(260, 220) };
            graphics.Update(points, new Native.Point { X = 260, Y = 220 }, default, "独立绘制", true);
            graphics.ProbeAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Check(graphics.RenderedFrames == 1 && graphics.Failures == 0, "native trajectory and hint render successfully on the graphics thread");
            var blocking = graphics.ProbeAsync(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); });
            Check(started.Wait(TimeSpan.FromSeconds(2)), "graphics fixture reached the compositor stall");
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) graphics.Update(points, new Native.Point { X = 260, Y = 220 }, default, "", true);
            graphics.Clear(); watch.Stop();
            Check(watch.ElapsedMilliseconds < 100, "1000 frame submissions never wait for a blocked graphics thread");
            using (var input = new GlobalInput((_, _) => false))
            {
                input.ProbeAsync().WaitAsync(TimeSpan.FromMilliseconds(250)).GetAwaiter().GetResult();
                Check(input.CallbackFailures == 0, "system input remains responsive during a graphics stall");
            }
            release.Set(); blocking.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            graphics.ProbeAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Check(graphics.RenderedFrames == 2, "stalled renderer discards all old frames and processes the final clear only");
            Check(graphics.Failures == 0, "graphics thread accesses no WPF objects across threads");
            Console.WriteLine($"GRAPHICS: 1000 latest-frame submissions during a render stall: {watch.Elapsed.TotalMilliseconds:F2} ms");
        }
        finally { release.Set(); graphics.Dispose(); graphics.Stopped.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        Check(graphics.Stopped.IsCompletedSuccessfully, "graphics HWNDs and buffers are released on their owning thread");
        Console.WriteLine($"PASS: {count} graphics-thread integration checks (blocked renderer, bounded latest frame, independent input, shutdown)");
    }
    private static void PumpFor(int milliseconds) => PumpUntil(() => false, milliseconds);
    private static void PumpUntil(Func<bool> done, int milliseconds)
    {
        var frame = new DispatcherFrame(); var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (done() || watch.ElapsedMilliseconds >= milliseconds) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
}
