using System.Collections.Concurrent;
using System.Threading;

namespace MouseOrbit;

internal sealed record MouseInputContext(bool Enabled, string Trigger, bool Gestures, bool WheelOpen, bool QuickOpen);
internal enum MouseStart { None, Trigger, Gesture }
internal readonly record struct MouseSample(int Message, Native.MouseData Data, long Time, nint Target, bool Consumed, MouseStart Start = MouseStart.None);

// Hook-thread state owns suppression and matching releases. UI-thread state owns all actions.
// The two sides exchange immutable snapshots and a bounded, asynchronous motion queue.
internal sealed class MouseInputBridge
{
    internal const int MaximumPendingMoves = 512;
    private readonly ConcurrentQueue<MouseSample> _pending = new();
    private readonly Action _wake;
    private readonly Func<nint> _foreground;
    private readonly Func<Native.Point, nint> _rootAt;
    private readonly Func<nint, bool> _isOwn;
    private MouseInputContext _context = new(false, "XButton1", false, false, false);
    private int _scheduled, _pendingMoves, _overflowed;
    private long _latest, _lastMotion;
    private bool _trigger, _right, _leftUp, _rightUp;
    private int _triggerUp;
    private uint _triggerButton;
    public MouseInputBridge(Action wake, Native.Point initial, Func<nint>? foreground = null, Func<Native.Point, nint>? rootAt = null, Func<nint, bool>? isOwn = null)
    {
        _wake = wake; _foreground = foreground ?? Native.GetForegroundWindow;
        _rootAt = rootAt ?? (point => WindowNative.GetAncestor(WindowNative.WindowFromPoint(point), 2));
        _isOwn = isOwn ?? WindowNative.IsOwn; _latest = Pack(initial);
    }
    private static long Pack(Native.Point point) => ((long)point.X << 32) | (uint)point.Y;
    public Native.Point Latest
    {
        get { long packed = Interlocked.Read(ref _latest); return new() { X = (int)(packed >> 32), Y = unchecked((int)packed) }; }
    }
    public void Publish(MouseInputContext context) => Volatile.Write(ref _context, context);
    public bool HasPending => !_pending.IsEmpty;
    public bool Handle(int message, Native.MouseData data) => Process(message, data, Environment.TickCount64);
    internal bool Process(int message, Native.MouseData data, long now)
    {
        Interlocked.Exchange(ref _latest, Pack(data.Pt));
        if (message == 0x200)
        {
            // Ordinary movement never wakes WPF; gesture samples are capped at 250 Hz.
            if ((_right || _trigger) && now - _lastMotion >= 4)
            { _lastMotion = now; Enqueue(new(message, data, now, 0, false)); }
            return false;
        }
        bool captured = false, notify = false; nint target = 0; var start = MouseStart.None;
        if (_trigger && message == _triggerUp && (message != 0x20C || (data.Mouse >> 16) == _triggerButton))
        { _trigger = false; captured = notify = true; }
        else if (message == 0x205 && (_right || _rightUp))
        { _right = _rightUp = false; captured = notify = true; }
        else if (message == 0x202 && _leftUp)
        { _leftUp = false; captured = notify = true; }
        else
        {
            var context = Volatile.Read(ref _context);
            if (!context.Enabled) return false;
            bool trigger = TriggerButtons.Matches(context.Trigger, message, data.Mouse);
            if (_trigger && trigger) return true; // Driver repeats must not start a second press.
            if (_right && message == 0x204) return true;
            if (_trigger && message is 0x201 or 0x204)
            { if (message == 0x201) _leftUp = true; else _rightUp = true; captured = notify = true; }
            else if (context.QuickOpen && message == 0x204)
            { _rightUp = true; captured = notify = true; }
            else if (_right && message == 0x201)
            { _leftUp = true; captured = notify = true; }
            else if (message == 0x20A && (_trigger || context.WheelOpen)) captured = notify = true;
            else if (context.WheelOpen && message is 0x201 or 0x204)
            { if (message == 0x201) _leftUp = true; else _rightUp = true; captured = notify = true; }
            else if (trigger && !_right && !_trigger)
            {
                _trigger = true; _triggerUp = message == 0x207 ? 0x208 : 0x20C; _triggerButton = data.Mouse >> 16;
                _lastMotion = now; captured = notify = true; start = MouseStart.Trigger; target = _foreground();
            }
            else if (message == 0x204 && context.Gestures && !_trigger)
            {
                target = _rootAt(data.Pt);
                if (!_isOwn(target)) { _right = true; _lastMotion = now; captured = notify = true; start = MouseStart.Gesture; }
            }
            else if (context.QuickOpen && message == 0x201) notify = true;
        }
        if (notify) Enqueue(new(message, data, now, target, captured, start));
        return captured;
    }
    private void Enqueue(MouseSample sample)
    {
        if (sample.Message == 0x200)
        {
            if (Volatile.Read(ref _pendingMoves) >= MaximumPendingMoves) { Interlocked.Exchange(ref _overflowed, 1); return; }
            Interlocked.Increment(ref _pendingMoves);
        }
        _pending.Enqueue(sample); Schedule();
    }
    private void Schedule() { if (Interlocked.Exchange(ref _scheduled, 1) == 0) _wake(); }
    public List<MouseSample> Drain(out bool overflowed)
    {
        var samples = new List<MouseSample>();
        while (_pending.TryDequeue(out var sample))
        { if (sample.Message == 0x200) Interlocked.Decrement(ref _pendingMoves); samples.Add(sample); }
        overflowed = Interlocked.Exchange(ref _overflowed, 0) != 0;
        Interlocked.Exchange(ref _scheduled, 0);
        if (!_pending.IsEmpty) Schedule();
        return samples;
    }
}
