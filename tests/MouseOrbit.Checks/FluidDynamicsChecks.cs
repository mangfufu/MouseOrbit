using MouseOrbit;
using System.Windows;
using System.Windows.Media;

internal static class FluidDynamicsChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        var spring = new FluidSpring(0); double peak = 0;
        for (int i = 0; i < 120; i++) { spring.Step(1, 1d / 120); peak = Math.Max(peak, spring.Value); }
        Check(peak > 1 && peak < 1.08 && Math.Abs(spring.Value - 1) < .001, "elastic response has one restrained overshoot and settles without jitter");
        spring = new FluidSpring(0); for (int i = 0; i < 9; i++) spring.Step(1, 1d / 120);
        double velocity = spring.Velocity, position = spring.Value; spring.Step(0, 1d / 120);
        Check(velocity > 0 && spring.Velocity > 0 && spring.Value > position, "rapid retargeting preserves momentum instead of restarting from zero velocity");
        var sixty = new FluidSpring(0); var high = new FluidSpring(0);
        for (int i = 0; i < 60; i++) sixty.Step(1, 1d / 60);
        for (int i = 0; i < 144; i++) high.Step(1, 1d / 144);
        Check(Math.Abs(sixty.Value - high.Value) < .00001, "spring timing is consistent on 60 and 144 Hz displays");
        spring.Step(-1, 30);
        Check(double.IsFinite(spring.Value) && double.IsFinite(spring.Velocity), "a delayed display frame cannot destabilize the spring");
        var water = new FlowState(42); var control = new FlowState(42);
        Point idle = water.Sample(.5, 1, 236, 60); water.SetPointer(new Point(118, idle.Y + 16));
        for (int i = 0; i < 60; i++) { water.Step(1d / 60); control.Step(1d / 60); }
        Check(Math.Abs(water.Sample(.5, 1, 236, 60).Y - control.Sample(.5, 1, 236, 60).Y) > 2,
            "a nearby pointer bends the shared stream rather than scattering unrelated dots");
        water.SetPointer(null);
        for (int i = 0; i < 120; i++) { water.Step(1d / 60); control.Step(1d / 60); }
        Check((water.Sample(.5, 1, 236, 60) - control.Sample(.5, 1, 236, 60)).Length < .01,
            "flow rejoins its autonomous path smoothly after the pointer leaves");
        var a = water.Sample(.49, 1, 236, 60); var b = water.Sample(.5, 1, 236, 60); var c = water.Sample(.51, 1, 236, 60);
        Check((a - b).Length < 3 && (b - c).Length < 3 && Math.Abs((a.Y + c.Y) / 2 - b.Y) < .05,
            "neighboring particles share one continuous path without discontinuities");
        var visual = new DrawingVisual(); var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 240; i++)
        { water.Step(1d / 60); using var dc = visual.RenderOpen(); LiquidPaint.Draw(dc, water, 236, 60); }
        watch.Stop();
        Console.WriteLine($"FLOW: retained stream drawing {watch.Elapsed.TotalMilliseconds / 240:F3} ms per row (CPU command recording only)");
        Console.WriteLine($"PASS: {count} fluid dynamics checks (spring continuity, cadence, pointer deflection, autonomous recovery)");
    }
}
