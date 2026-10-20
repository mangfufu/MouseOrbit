using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MouseOrbit;

// Layered-window uploads can wait for the desktop compositor. Keep those waits off
// both WPF's dispatcher and the synchronous system mouse-hook thread.
internal sealed class GestureRenderer : IDisposable
{
    private sealed record Frame(Point[] Points, Native.Point Cursor, WindowTarget Target, string Name, bool Global, OverlayColors Colors, long Epoch);
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly OverlayColors _initialColors;
    private Dispatcher? _dispatcher;
    private Frame? _latest;
    private PointerTrail? _trail;
    private GestureHint? _hint;
    private int _scheduled, _disposed, _failures;
    private long _rendered;
    private long _epoch, _paintedEpoch = -1;
    internal Task Ready => _ready.Task;
    internal Task Stopped => _stopped.Task;
    internal long RenderedFrames => Interlocked.Read(ref _rendered);
    internal int Failures => Volatile.Read(ref _failures);
    internal int ManagedThreadId => _thread.ManagedThreadId;
    internal event Action<string>? Error;
    internal GestureRenderer()
    {
        _initialColors = OverlayColors.Capture();
        _thread = new Thread(Run) { IsBackground = true, Name = "MouseOrbit gesture graphics" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
    }
    internal void Update(IReadOnlyList<Point> points, Native.Point cursor, WindowTarget target, string name, bool global)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _latest, new Frame(points.ToArray(), cursor, target, name, global, OverlayColors.Capture(), Interlocked.Read(ref _epoch)));
        Schedule();
    }
    internal void Clear()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _latest, new Frame([], default, default, "", false, default, Interlocked.Increment(ref _epoch)));
        Schedule();
    }
    private void Schedule()
    {
        var dispatcher = Volatile.Read(ref _dispatcher);
        if (dispatcher == null || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _scheduled, 1) != 0) return;
        dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RenderLatest));
    }
    private void Run()
    {
        try
        {
            using var trail = new PointerTrail(); using var hint = new GestureHint();
            _trail = trail; _hint = hint;
            trail.Prepare(); hint.Prepare(_initialColors);
            Volatile.Write(ref _dispatcher, Dispatcher.CurrentDispatcher);
            _ready.TrySetResult();
            if (Volatile.Read(ref _disposed) != 0) return;
            if (Volatile.Read(ref _latest) != null) Schedule();
            Dispatcher.Run();
        }
        catch (Exception ex) { Interlocked.Increment(ref _failures); _ready.TrySetException(ex); Error?.Invoke("手势绘制失败：" + ex.Message); }
        finally { _trail = null; _hint = null; _stopped.TrySetResult(); }
    }
    private void RenderLatest()
    {
        try
        {
            var frame = Interlocked.Exchange(ref _latest, null);
            if (frame == null || Volatile.Read(ref _disposed) != 0) return;
            if (_paintedEpoch != frame.Epoch) { _trail!.Clear(); _hint!.Clear(); _paintedEpoch = frame.Epoch; }
            if (frame.Points.Length < 2) { _trail!.Clear(); _hint!.Clear(); }
            else
            {
                _trail!.Path.Begin(frame.Points[0]);
                foreach (var point in frame.Points.Skip(1)) _trail.Path.Add(point);
                _trail.Refresh(frame.Colors.Accent);
                // A release/cancel arriving during a DWM upload wins over the old hint.
                if (Volatile.Read(ref _latest) is { Points.Length: < 2 }) { _trail.Clear(); _hint!.Clear(); }
                else if (frame.Name.Length == 0) _hint!.Clear();
                else _hint!.Update(frame.Cursor, frame.Target, frame.Name, frame.Global, frame.Colors);
            }
            Interlocked.Increment(ref _rendered);
        }
        catch (Exception ex) { Interlocked.Increment(ref _failures); Error?.Invoke("手势绘制失败：" + ex.Message); }
        finally
        {
            Volatile.Write(ref _scheduled, 0);
            if (Volatile.Read(ref _latest) != null) Schedule();
        }
    }
    internal Task ProbeAsync(Action? action = null)
    {
        var dispatcher = Volatile.Read(ref _dispatcher);
        return dispatcher == null ? Task.FromException(new InvalidOperationException("Graphics thread is not ready")) :
            dispatcher.InvokeAsync(action ?? (() => { }), DispatcherPriority.ApplicationIdle).Task;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Exchange(ref _latest, null);
        Volatile.Read(ref _dispatcher)?.BeginInvokeShutdown(DispatcherPriority.Send);
    }
}
