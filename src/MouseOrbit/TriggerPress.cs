namespace MouseOrbit;

internal enum TriggerRelease { None, Panel, Wheel }

// One press owns one result. Timer promotion and mouse-up use the same boundary.
internal sealed class TriggerPress
{
    internal const double WheelDistance = 55;
    private long _started;
    private bool _active, _held, _canceled, _dismissOnTap;
    public int Version { get; private set; }
    public void Begin(long now, bool dismissOnTap)
    { Version++; _started = now; _active = true; _held = _canceled = false; _dismissOnTap = dismissOnTap; }
    public bool TryHold(long now, double dx, double dy)
    {
        if (!_active || _held || _canceled) return false;
        // Small movements while reaching for a side button must not turn a tap into a canceled wheel.
        if (now - _started < PointerPress.HoldMilliseconds && dx * dx + dy * dy < WheelDistance * WheelDistance) return false;
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

internal static class TriggerButtons
{
    internal static bool Matches(string trigger, int message, uint mouseData) => trigger switch
    {
        "XButton1" => message == 0x20B && (mouseData >> 16) == 1,
        "XButton2" => message == 0x20B && (mouseData >> 16) == 2,
        "Middle" => message == 0x207,
        _ => false
    };
}
