using System.Windows.Media;

namespace MouseOrbit;

// Exact damped spring solution: retargeting keeps velocity and does not restart an easing curve.
internal struct FluidSpring(double value)
{
    internal double Value = value, Velocity;
    internal void Set(double value) { Value = value; Velocity = 0; }
    internal void Step(double target, double seconds, double frequency = 18, double damping = .72)
    {
        double dt = Math.Clamp(seconds, 0, .05), a = -damping * frequency;
        double b = frequency * Math.Sqrt(1 - damping * damping), x = Value - target;
        double c = (Velocity - a * x) / b, sin = Math.Sin(b * dt), cos = Math.Cos(b * dt), decay = Math.Exp(a * dt);
        Value = target + decay * (x * cos + c * sin);
        Velocity = decay * ((a * x + b * c) * cos + (a * c - b * x) * sin);
    }
}

internal interface IFlowFrame { void AdvanceFlow(double seconds); }

// One display-synchronised callback for all small, visible materials; no cursor polling or timers.
internal static class FlowClock
{
    private static readonly HashSet<IFlowFrame> Clients = new();
    private static double _last = -1;
    internal static bool CaptureMode { get; set; }
    internal static int ActiveCount => Clients.Count;
    internal static void Add(IFlowFrame client)
    {
        if (!Clients.Add(client) || Clients.Count != 1) return;
        _last = -1; CompositionTarget.Rendering += Frame;
    }
    internal static void Remove(IFlowFrame client)
    {
        Clients.Remove(client);
        if (Clients.Count != 0) return;
        CompositionTarget.Rendering -= Frame; _last = -1;
    }
    private static void Frame(object? sender, EventArgs args)
    {
        if (CaptureMode) return;
        if (args is not RenderingEventArgs rendering) return;
        double now = rendering.RenderingTime.TotalSeconds, dt = _last < 0 ? 1d / 60 : now - _last;
        if (dt <= 0) return;
        _last = now;
        foreach (var client in Clients.ToArray()) if (Clients.Contains(client)) client.AdvanceFlow(Math.Min(dt, .05));
    }
    internal static void CaptureStep(double seconds)
    { foreach (var client in Clients.ToArray()) if (Clients.Contains(client)) client.AdvanceFlow(seconds); }
}

// Brief group expansion changes real row geometry, so visible rows and their click targets agree.
internal sealed class FluidHeight : IFlowFrame
{
    private static readonly Dictionary<FrameworkElement, FluidHeight> Active = new();
    private readonly FrameworkElement _view;
    private FluidSpring _height;
    private double _target, _delay;
    private bool _expanding;
    private Action? _completed;
    private FluidHeight(FrameworkElement view) { _view = view; _height = new(double.IsNaN(view.Height) ? view.ActualHeight : view.Height); }
    internal static void To(FrameworkElement view, double target, bool expanding, int delay, Action? completed)
    {
        if (!SystemParameters.ClientAreaAnimation) { Stop(view); view.Height = target; completed?.Invoke(); return; }
        if (!Active.TryGetValue(view, out var motion)) { motion = new(view); Active.Add(view, motion); FlowClock.Add(motion); }
        motion._target = target; motion._delay = delay / 1000d; motion._expanding = expanding; motion._completed = completed;
    }
    public void AdvanceFlow(double seconds)
    {
        if (_delay > 0) { _delay -= seconds; return; }
        _height.Step(_target, seconds, 20, _expanding ? .74 : .9);
        _view.Height = Math.Max(0, _height.Value);
        if ((_target == 0 && _height.Value < .12) || (Math.Abs(_height.Value - _target) < .03 && Math.Abs(_height.Velocity) < .15))
        { _view.Height = _target; Active.Remove(_view); FlowClock.Remove(this); var completed = _completed; _completed = null; completed?.Invoke(); }
    }
    internal static void Stop(FrameworkElement view)
    { if (Active.Remove(view, out var motion)) { FlowClock.Remove(motion); motion._completed = null; } }
}

internal sealed class FlowState(int seed)
{
    internal readonly double Phase = (seed & 1023) / 1024d;
    internal double Time { get; private set; }
    internal FluidSpring Focus = new(0), Pressure = new(0), Press = new(0), PointerX = new(0), PointerY = new(0), Join = new(0);
    private double _focus, _press, _join;
    private Point? _pointer;
    internal bool Settling => Math.Abs(Focus.Value - _focus) + Math.Abs(Focus.Velocity) + Math.Abs(Join.Value - _join) + Math.Abs(Join.Velocity) > .005;
    internal void SetFocus(bool focus, bool animate)
    { _focus = focus ? 1 : 0; if (!animate) Focus.Set(_focus); }
    internal void SetJoined(bool joined, bool animate)
    { _join = joined ? 1 : 0; if (!animate) Join.Set(_join); }
    internal void SetPress(bool press) => _press = press ? 1 : 0;
    internal void SetPointer(Point? pointer)
    {
        if (_pointer == null && pointer != null) { PointerX.Set(pointer.Value.X); PointerY.Set(pointer.Value.Y); }
        _pointer = pointer;
    }
    internal void Step(double seconds)
    {
        Time += Math.Clamp(seconds, 0, .05);
        Focus.Step(_focus, seconds); Join.Step(_join, seconds); Press.Step(_press, seconds, 24, .78);
        Pressure.Step(_pointer == null ? 0 : 1, seconds, 12, .88);
        if (_pointer is { } pointer) { PointerX.Step(pointer.X, seconds, 20, .83); PointerY.Step(pointer.Y, seconds, 20, .83); }
    }
    internal void Settle()
    { Focus.Set(_focus); Join.Set(_join); Press.Set(0); _press = 0; Pressure.Set(0); _pointer = null; }
    internal Point Sample(double t, int lane, double width, double height)
    {
        double x = 12 + t * Math.Max(1, width - 24), phase = Phase * 6.28;
        double y = height * .5 + (lane - 1) * height * .07;
        y += Math.Sin(t * 5.1 + Time * .55 + phase) * height * .12;
        y += Math.Sin(t * 2.8 - Time * .31 + phase + lane * .13) * height * .045;
        y += Join.Value * (lane - 1) * Math.Sin(t * Math.PI) * 3;
        double dx = x - PointerX.Value, dy = y - PointerY.Value;
        double influence = Math.Exp(-(dx * dx + dy * dy) / 3400) * Math.Clamp(Pressure.Value, 0, 1);
        // Nearby flow bends around a soft obstacle; the spring prevents a cursor-shaped discontinuity.
        y += influence * (dy / (Math.Abs(dy) + 18) * 10 + Math.Sin(dx / 27) * 3.5);
        return new Point(x, y);
    }
}

internal static class LiquidPaint
{
    private static Color _tone;
    private static Pen[] _trails = [];
    private static Brush _ribbon = Brushes.Transparent;
    private static Pen _thread = new(Brushes.Transparent, 1);
    private static readonly Brush Fade = FadeBrush();
    private static Brush FadeBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Black, .17));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Black, .77));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1)); brush.Freeze(); return brush;
    }
    private static void EnsurePalette()
    {
        if (_trails.Length != 0 && _tone == Palette.Ink.Color) return;
        _tone = Palette.Ink.Color; var accent = Palette.Accent.Color;
        byte Mix(byte a, byte b) => (byte)(a * .35 + b * .65);
        var pigment = Color.FromRgb(Mix(_tone.R, accent.R), Mix(_tone.G, accent.G), Mix(_tone.B, accent.B));
        Brush B(byte alpha) { var c = pigment; c.A = alpha; var b = new SolidColorBrush(c); b.Freeze(); return b; }
        Pen P(byte alpha, double thickness) { var p = new Pen(B(alpha), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }
        _ribbon = B(Palette.Dark ? (byte)12 : (byte)10); _thread = P(Palette.Dark ? (byte)45 : (byte)34, .65);
        _trails = [P(30, .8), P(65, .95), P(115, 1.05)];
    }
    private static StreamGeometry Curve(Point[] points, double offset = 0, bool filled = false)
    {
        var path = new StreamGeometry();
        using (var dc = path.Open())
        {
            dc.BeginFigure(new Point(points[0].X, points[0].Y - offset), filled, filled);
            void Side(bool reverse, double dy)
            {
                for (int i = 0; i < points.Length - 1; i++)
                {
                    int a = reverse ? points.Length - 1 - i : i, b = reverse ? a - 1 : a + 1;
                    Point p0 = points[Math.Clamp(reverse ? a + 1 : a - 1, 0, points.Length - 1)], p1 = points[a], p2 = points[b], p3 = points[Math.Clamp(reverse ? b - 1 : b + 1, 0, points.Length - 1)];
                    var c1 = p1 + (p2 - p0) / 6; var c2 = p2 - (p3 - p1) / 6;
                    dc.BezierTo(new(c1.X, c1.Y + dy), new(c2.X, c2.Y + dy), new(p2.X, p2.Y + dy), true, false);
                }
            }
            Side(false, -offset);
            if (filled) { dc.LineTo(new(points[^1].X, points[^1].Y + offset), true, false); Side(true, offset); }
        }
        path.Freeze(); return path;
    }
    internal static void Draw(DrawingContext dc, FlowState state, double width, double height, bool plate = false)
    {
        if (width < 20 || height < 10) return;
        EnsurePalette(); double focus = Math.Clamp(state.Focus.Value, 0, 1.08);
        dc.PushOpacityMask(Fade); dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (focus > .001 || plate)
        {
            var color = Palette.Surface.Color; color.A = (byte)((plate ? 120 : 68) * (plate ? 1 : focus));
            var lens = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B)) { RadiusX = .5, RadiusY = .5 };
            double stretch = 1 + focus * .025 - state.Press.Value * .025;
            dc.DrawEllipse(lens, null, new Point(width * .46, height * .51), (width * .49) * stretch, height * (plate ? .58 : .58));
        }
        for (int lane = 0; lane < 3; lane++)
        {
            var points = new Point[9]; for (int i = 0; i < points.Length; i++) points[i] = state.Sample(i / 8d, lane, width, height);
            dc.PushOpacity((plate ? .55 : .65) + focus * .28);
            dc.DrawGeometry(_ribbon, null, Curve(points, 2 + focus, true));
            dc.PushOpacity(lane == 1 ? .75 : .35); dc.DrawGeometry(null, _thread, Curve(points)); dc.Pop();
            for (int i = 0; i < 8; i++)
            {
                double t = (i / 8d + lane * .173 + state.Phase + state.Time * (.067 + lane * .006)) % 1;
                double fade = Math.Pow(Math.Sin(t * Math.PI), 1.8);
                dc.PushOpacity(fade);
                for (int segment = 0; segment < 3; segment++)
                {
                    double a = Math.Max(0, t - (3 - segment) * .009), b = Math.Max(0, t - (2 - segment) * .009);
                    dc.DrawLine(_trails[segment], state.Sample(a, lane, width, height), state.Sample(b, lane, width, height));
                }
                dc.Pop();
            }
            dc.Pop();
        }
        dc.Pop();
    }
}

internal sealed class LiquidFlow : FrameworkElement, IFlowFrame
{
    internal FlowState State { get; }
    private readonly bool _vertical;
    private bool _presented;
    internal bool Running { get; private set; }
    internal event Action? Advanced;
    internal LiquidFlow(int seed, bool vertical = false)
    {
        State = new(seed); _vertical = vertical; IsHitTestVisible = false; ParticleTone.Watch(this);
        Loaded += (_, _) => UpdateClock();
        Unloaded += (_, _) => { _presented = false; UpdateClock(); };
        IsVisibleChanged += (_, _) => UpdateClock();
    }
    internal void SetPresented(bool presented) { _presented = presented; UpdateClock(); }
    private void UpdateClock()
    {
        bool run = _presented && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (run == Running) return;
        Running = run;
        if (run) FlowClock.Add(this); else { FlowClock.Remove(this); State.Settle(); Advanced?.Invoke(); InvalidateVisual(); }
    }
    internal void SetPointer(Point? point)
    { if (point != null && _vertical) point = new Point(point.Value.Y, RenderSize.Width - point.Value.X); State.SetPointer(point); }
    public void AdvanceFlow(double seconds) { State.Step(seconds); Advanced?.Invoke(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (_vertical)
        { dc.PushOpacity(.38); dc.PushTransform(new MatrixTransform(0, 1, -1, 0, RenderSize.Width, 0)); LiquidPaint.Draw(dc, State, RenderSize.Height, RenderSize.Width); dc.Pop(); dc.Pop(); }
        else LiquidPaint.Draw(dc, State, RenderSize.Width, RenderSize.Height);
    }
}
