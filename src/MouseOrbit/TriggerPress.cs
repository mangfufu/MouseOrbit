namespace MouseOrbit;

internal enum TriggerRelease { None, Panel, Wheel }

// One press owns one result. Timer promotion and mouse-up use the same boundary.
internal sealed class TriggerPress
{
    private long _started;
    private bool _active, _held, _canceled, _dismissOnTap;
    public int Version { get; private set; }
    public void Begin(long now, bool dismissOnTap)
    { Version++; _started = now; _active = true; _held = _canceled = false; _dismissOnTap = dismissOnTap; }
    public bool TryHold(long now, double dx, double dy)
    {
        if (!_active || _held || _canceled) return false;
        if (now - _started < PointerPress.HoldMilliseconds && dx * dx + dy * dy < PointerPress.TapDistance * PointerPress.TapDistance) return false;
        _held = true; return true;
    }
    public TriggerRelease End(long now, double dx, double dy)
    {
        if (!_active) return TriggerRelease.None;
        TryHold(now, dx, dy); _active = false;
        return _canceled ? TriggerRelease.None : _held ? TriggerRelease.Wheel : _dismissOnTap ? TriggerRelease.None : TriggerRelease.Panel;
    }
    public void Cancel() { Version++; _canceled = true; }
}
