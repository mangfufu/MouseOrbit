using MouseOrbit;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PreloadChecks
{
    internal static void RunStartup()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        using var session = new LauncherSession(new Launcher());
        try
        {
            int windowsBefore = System.Windows.Application.Current.Windows.Count;
            var watch = System.Diagnostics.Stopwatch.StartNew(); var task = session.RefreshAsync();
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (task.IsCompleted || watch.Elapsed > TimeSpan.FromSeconds(30)) frame.Continue = false; };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
            if (!task.IsCompleted) throw new Exception("Startup preload exceeded the test timeout");
            task.GetAwaiter().GetResult(); double load = watch.Elapsed.TotalMilliseconds;
            var quick = session.BuildQuickEntries().Values.SelectMany(entries => entries).Where(entry => entry.Kind != "command").ToArray();
            if (quick.Any(entry => !ShellIcons.TryGet(entry, out _))) throw new Exception("FAIL: quick entries were not warmed before startup completed");
            if (System.Windows.Application.Current.Windows.Count != windowsBefore) throw new Exception("FAIL: background launcher created a management window");
            Console.WriteLine($"STARTUP: {quick.Length} quick-entry icons ready in {load:F1} ms; management windows created: 0");
            Console.WriteLine("PASS: 2 actual startup checks (bounded icon preload, no management UI)");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    internal static void Run()
    {
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; }
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4); image.Freeze();
        var cache = new IconCache(1024); int reads = 0;
        Task<ImageSource?> Read() { Interlocked.Increment(ref reads); return Task.FromResult<ImageSource?>(image); }
        var first = cache.Get("app:0", Read);
        for (int i = 1; i < 800; i++) cache.Get("app:" + i, Read).GetAwaiter().GetResult();
        Check(ReferenceEquals(first, cache.Get("APP:0", Read)) && reads == 800, "preloading 800 apps preserves the first cached icon without a 512-entry reset");
        var missing = cache.Get("missing", () => Task.FromResult<ImageSource?>(null));
        Check(cache.TryGet("missing", out var fallback) && fallback == null && ReferenceEquals(missing, cache.Get("missing", Read)), "failed extraction is cached and does not repeatedly read the shell");
        Parallel.For(0, 32, _ => cache.Get("concurrent", Read).GetAwaiter().GetResult());
        Check(reads == 801, "concurrent icon requests perform one extraction per key");
        var small = new IconCache(2); var pending = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        small.Get("pending", () => pending.Task);
        small.Get("second", Read); small.Get("third", Read);
        Check(ReferenceEquals(pending.Task, small.Get("pending", Read)), "cache pressure never evicts an icon extraction in flight");
        pending.SetResult(image);
        var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            int scans = 0; bool fail = false; using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var windows = new List<SwitchWindow> { new(100, 1, "app", "应用", "旧窗口", false, 1, 1) };
            var groups = new List<SwitchGroup> { new("app", "应用", windows) };
            var snapshots = new WindowSnapshotCache(() =>
            {
                int scan = Interlocked.Increment(ref scans);
                if (scan == 2) { started.Set(); release.Wait(TimeSpan.FromSeconds(3)); }
                if (fail) throw new IOException("fixture read failure");
                return groups;
            });
            snapshots.EnsureReadyAsync().GetAwaiter().GetResult();
            windows.Add(new(101, 1, "app", "应用", "新窗口", false, 1, 2));
            Check(snapshots.Current[0].Windows.Count == 1, "published window snapshots do not change while a new scan is being built");
            var refresh = snapshots.RefreshAsync();
            try
            {
                Check(started.Wait(TimeSpan.FromSeconds(2)), "window refresh is running on a background thread");
                Check(ReferenceEquals(refresh, snapshots.RefreshAsync()) && scans == 2, "simultaneous refresh requests share one native scan");
                Check(snapshots.EnsureReadyAsync().IsCompletedSuccessfully && snapshots.Current[0].Windows.Count == 1, "opening reads the prepared window list without waiting for a slow refresh");
            }
            finally { release.Set(); }
            refresh.GetAwaiter().GetResult();
            Check(snapshots.Current[0].Windows.Count == 2, "a completed scan publishes the new windows together");
            fail = true;
            try { snapshots.RefreshAsync().GetAwaiter().GetResult(); throw new Exception("Expected fixture failure"); }
            catch (IOException) { }
            Check(snapshots.Ready && snapshots.Current[0].Windows.Count == 2, "a failed background scan preserves the usable cached page");
            var iconsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gated = new WindowSnapshotCache(() => groups, _ => iconsReady.Task); var loading = gated.EnsureReadyAsync();
            Check(!gated.Ready && gated.Current.Count == 0 && !loading.IsCompleted, "new window data is not published while its application icons are still loading");
            iconsReady.SetResult(); loading.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MouseOrbit-preload-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var launcher = new Launcher(directory); var old = new DesktopEntry("old", "已准备", "", "command", "downloads", "");
            launcher.ApplyScan(new[] { old }); var scan = launcher.CreateScan(); var candidates = Task.Run(scan).GetAwaiter().GetResult();
            Check(launcher.Entries.SequenceEqual(new[] { old }), "reading new applications never mutates the published launcher list");
            launcher.ApplyScan(candidates);
            Check(launcher.Entries.SequenceEqual(candidates), "the prepared launcher scan is published explicitly after preloading");
            launcher.ApplyScan(new[] { old });
            using var session = new LauncherSession(launcher); int changes = 0;
            session.Changed += () => changes++;
            session.Record(old);
            var quick = session.BuildQuickEntries();
            Check(quick["recent"].Any(e => e.Id == old.Id) && quick["frequent"].Any(e => e.Id == old.Id), "headless usage recording updates recent and frequent without constructing a management window");
            Check(session.Remove(old, true) && !session.BuildQuickEntries()["frequent"].Any(e => e.Id == old.Id), "headless frequent removal immediately updates side-panel entries");
            Check(session.Remove(old, false) && !session.BuildQuickEntries()["recent"].Any(e => e.Id == old.Id), "headless recent removal immediately updates side-panel entries");
            session.Record(old);
            Check(session.BuildQuickEntries()["recent"].Any(e => e.Id == old.Id) && !session.BuildQuickEntries()["frequent"].Any(e => e.Id == old.Id), "reopening restores recency while respecting removal from frequent");
            var restored = new Launcher(directory); restored.ApplyScan(new[] { old });
            Check(restored.Recent().Any(e => e.Id == old.Id) && restored.IsHiddenFromFrequent(old) && changes == 4, "usage and removal persist across sessions without any management UI");
        }
        finally { System.IO.Directory.Delete(directory, true); }
        Console.WriteLine($"PASS: {count} preload checks (800-app cache, pending icons, prepared snapshots, background publication)");
    }
}
