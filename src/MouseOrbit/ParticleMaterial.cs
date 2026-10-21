using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MouseOrbit;

// Protect lettering rather than placing a rectangular panel behind every row.
internal sealed class ParticleLabel : Decorator
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(ParticleLabel),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (sender, args) => ((ParticleLabel)sender)._text.Text = args.NewValue as string ?? ""));
    private readonly TextBlock _text = new() { FontFamily = Palette.BrandFont, FontSize = 13, Foreground = Palette.Ink, IsHitTestVisible = false };
    public ParticleLabel() { Child = _text; IsHitTestVisible = false; }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => _text.FontSize; set => _text.FontSize = value; }
    public Brush Foreground { get => _text.Foreground; set => _text.Foreground = value; }
    public TextTrimming TextTrimming { get => _text.TextTrimming; set => _text.TextTrimming = value; }
    public TextAlignment TextAlignment { get => _text.TextAlignment; set => _text.TextAlignment = value; }
    private string? _key;
    private Geometry? _letters;
    protected override void OnRender(DrawingContext drawing)
    {
        if (ActualWidth > 1 && !string.IsNullOrEmpty(Text))
        {
            string key = $"{Text}|{ActualWidth}|{FontSize}|{_text.FontFamily}|{_text.FontWeight}|{TextAlignment}";
            if (_key != key)
            {
                var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection,
                    new Typeface(_text.FontFamily, _text.FontStyle, _text.FontWeight, _text.FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                { MaxTextWidth = ActualWidth, MaxLineCount = 1, Trimming = TextTrimming, TextAlignment = TextAlignment };
                _letters = text.BuildGeometry(new Point()); _letters.Freeze(); _key = key;
            }
            drawing.DrawGeometry(null, new Pen(Palette.Surface, 2.6) { LineJoin = PenLineJoin.Round }, _letters);
        }
        base.OnRender(drawing);
    }
}

internal sealed class ParticleIcon(string kind) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var icon = (DrawingImage)CommandIcons.Get(kind);
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        if (icon.Drawing is DrawingGroup group)
            foreach (var drawing in group.Children.OfType<GeometryDrawing>().Where(d => d.Pen != null))
                dc.DrawGeometry(null, new Pen(Palette.Surface, 4.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, drawing.Geometry);
        dc.DrawDrawing(icon.Drawing); dc.Pop();
    }
}

// Frozen material and small dust drawings; clocks move only the material, never the hit targets.
internal sealed class ParticleField : Grid
{
    private readonly Layer _rest, _gather, _joined;
    private readonly TranslateTransform _shift = new();
    private readonly TranslateTransform _restShift = new();
    private readonly ParticleDrift _dust;
    private readonly List<AnimationClock> _ambientClocks = new();
    private bool _active;
    private bool _connected;
    private bool _transitioning, _revealing, _presented;
    private int _transition, _reveal;
    internal bool AmbientRunning => _dust.Running;
    internal bool MotionRunning => AmbientRunning || _transitioning || _revealing;
    internal ParticleField(int seed, double width = 236, double height = 60)
    {
        Width = width; Height = height; HorizontalAlignment = HorizontalAlignment.Left; IsHitTestVisible = false;
        _rest = new Layer(ParticleTexture.Get(ParticleTexture.Form.Rest, seed, width, height)) { Opacity = .74, RenderTransform = _restShift };
        _gather = new Layer(ParticleTexture.Get(ParticleTexture.Form.Gather, seed, width, height)) { Opacity = 0, RenderTransform = _shift };
        _joined = new Layer(ParticleTexture.Get(ParticleTexture.Form.Joined, seed, width, height)) { Opacity = 0 };
        _dust = new ParticleDrift(seed, width, height);
        Children.Add(_rest); Children.Add(_gather); Children.Add(_joined); Children.Add(_dust);
        Loaded += (_, _) => { if (_presented) SetPresented(true); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); };
        Unloaded += (_, _) => Stop();
    }
    internal void SetActive(bool active, bool animate = true)
    {
        if (_active == active && animate) return;
        _active = active;
        Blend(animate);
    }
    internal void SetJoined(bool connected)
    {
        if (_connected == connected) return;
        _connected = connected; Blend(true);
    }
    private bool CanAnimate => SystemParameters.ClientAreaAnimation && IsLoaded && IsVisible;
    private void Blend(bool animate)
    {
        int version = ++_transition; _transitioning = animate && CanAnimate;
        Fade(_rest, .74); Fade(_joined, _active && _connected ? .7 : 0); Fade(_gather, _active && !_connected ? .82 : 0, true);
        void Fade(UIElement layer, double target, bool complete = false)
        {
            double from = layer.Opacity;
            layer.BeginAnimation(OpacityProperty, null); layer.Opacity = target;
            if (!_transitioning) return;
            var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(160))
            { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (complete) animation.Completed += (_, _) => { if (version == _transition) { _transitioning = false; ClearBlends(); } };
            layer.BeginAnimation(OpacityProperty, animation);
        }
    }
    internal void Pulse(bool animate = true, int delay = 0)
    {
        int version = ++_reveal; BeginAnimation(OpacityProperty, null); Opacity = 1;
        _revealing = animate && CanAnimate;
        if (!_revealing) return;
        var reveal = new DoubleAnimation(.12, 1, TimeSpan.FromMilliseconds(180))
        { BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        reveal.Completed += (_, _) => { if (version == _reveal) { _revealing = false; BeginAnimation(OpacityProperty, null); } };
        BeginAnimation(OpacityProperty, reveal);
    }
    internal void SetPresented(bool presented)
    {
        _presented = presented;
        if (!presented || !CanAnimate) { Stop(); return; }
        if (_dust.Running) return;
        _dust.Start();
        _ambientClocks.Add(ParticleDrift.Loop(_restShift, TranslateTransform.XProperty, -.65, .85, 7.6));
        _ambientClocks.Add(ParticleDrift.Loop(_shift, TranslateTransform.YProperty, -.65, .65, 6.2));
    }
    private void ClearBlends()
    { _rest.BeginAnimation(OpacityProperty, null); _gather.BeginAnimation(OpacityProperty, null); _joined.BeginAnimation(OpacityProperty, null); }
    internal void Stop()
    {
        _presented = false; ++_transition; ++_reveal; _transitioning = _revealing = false;
        foreach (var clock in _ambientClocks) clock.Controller?.Remove(); _ambientClocks.Clear();
        _dust.Stop(); ClearBlends(); BeginAnimation(OpacityProperty, null); Opacity = 1;
        _restShift.BeginAnimation(TranslateTransform.XProperty, null); _shift.BeginAnimation(TranslateTransform.YProperty, null);
        _rest.Opacity = .74; _gather.Opacity = _active && !_connected ? .82 : 0; _joined.Opacity = _active && _connected ? .7 : 0;
    }
    private sealed class Layer(Drawing drawing) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(drawing);
    }
}

// Independent slow arcs avoid the appearance of a single bitmap sliding around a card.
// Eight tiny retained drawings per visible row, no frame handler, simulation, blur or layout work.
internal sealed class ParticleDrift : Canvas
{
    private readonly List<(FrameworkElement View, TranslateTransform Shift, double Phase)> _motes = new();
    private readonly List<AnimationClock> _clocks = new();
    internal bool Running { get; private set; }
    internal ParticleDrift(int seed, double width, double height)
    {
        IsHitTestVisible = false; Clip = new RectangleGeometry(new Rect(0, 0, width, height));
        var random = new Random(seed & int.MaxValue);
        for (int i = 0; i < 8; i++)
        {
            double phase = random.NextDouble();
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                for (int n = 0; n < 4; n++)
                {
                    var color = Palette.Ink.Color; color.A = (byte)(70 + random.Next(75));
                    var brush = new SolidColorBrush(color); brush.Freeze();
                    double radius = .2 + random.NextDouble() * .3;
                    dc.DrawEllipse(brush, null, new Point(1 + random.NextDouble() * 3, 1 + random.NextDouble() * 3), radius, radius);
                }
            }
            drawing.Freeze();
            var shift = new TranslateTransform();
            var mote = new Dust(drawing) { Width = 5, Height = 5, Opacity = 0, RenderTransform = shift };
            // Most dust stays in the icon crescent; a few grains drift across the option's thin edge.
            SetLeft(mote, i < 5 ? 6 + random.NextDouble() * 35 : 56 + random.NextDouble() * Math.Max(1, width - 74));
            SetTop(mote, i < 5 ? 9 + random.NextDouble() * Math.Max(1, height - 22) : (i % 2 == 0 ? 10 : height - 14));
            Children.Add(mote); _motes.Add((mote, shift, phase));
        }
        Unloaded += (_, _) => Stop();
    }
    internal static AnimationClock Loop(Animatable target, DependencyProperty property, double from, double to, double seconds, double phase = 0)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Timeline.SetDesiredFrameRate(animation, 30);
        var clock = (AnimationClock)animation.CreateClock(true); target.ApplyAnimationClock(property, clock);
        if (phase > 0) clock.Controller?.SeekAlignedToLastTick(TimeSpan.FromSeconds(phase * seconds * 2), TimeSeekOrigin.BeginTime);
        return clock;
    }
    // UIElement isn't Animatable; apply its opacity clock directly.
    internal void Start()
    {
        if (Running || !SystemParameters.ClientAreaAnimation || !IsLoaded || !IsVisible) return;
        Running = true;
        foreach (var (view, shift, phase) in _motes)
        {
            double duration = 3.8 + phase * 2.8;
            _clocks.Add(Loop(shift, TranslateTransform.XProperty, -2.4, 3.4, duration, phase));
            _clocks.Add(Loop(shift, TranslateTransform.YProperty, 1.5, -2.5, duration * 1.27, phase));
            var opacity = new DoubleAnimation(.24, .82, TimeSpan.FromSeconds(duration * .8))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(opacity, 30);
            var clock = (AnimationClock)opacity.CreateClock(true); view.ApplyAnimationClock(OpacityProperty, clock);
            _clocks.Add(clock);
            clock.Controller?.SeekAlignedToLastTick(TimeSpan.FromSeconds(phase * duration), TimeSeekOrigin.BeginTime);
        }
    }
    internal void Stop()
    {
        Running = false;
        foreach (var clock in _clocks) clock.Controller?.Remove(); _clocks.Clear();
        foreach (var (view, shift, _) in _motes)
        { view.BeginAnimation(OpacityProperty, null); shift.BeginAnimation(TranslateTransform.XProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null); }
    }
    private sealed class Dust(Drawing drawing) : FrameworkElement
    { protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(drawing); }
}

// The existing native menu retains its behavior, with a compact particulate side-wing surface.
public sealed class ParticleMenuPlate : Border
{
    private readonly TranslateTransform _drift = new();
    private AnimationClock? _clock;
    public ParticleMenuPlate()
    {
        Loaded += (_, _) => UpdateMotion();
        Unloaded += (_, _) => StopMotion();
        IsVisibleChanged += (_, _) => UpdateMotion();
    }
    private void UpdateMotion()
    {
        if (!IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation) { StopMotion(); return; }
        _clock ??= ParticleDrift.Loop(_drift, TranslateTransform.YProperty, -.55, .55, 7.2);
    }
    private void StopMotion()
    { _clock?.Controller?.Remove(); _clock = null; _drift.BeginAnimation(TranslateTransform.YProperty, null); }
    private Size _last;
    private Drawing? _drawing;
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 2 || ActualHeight < 2) return;
        if (_drawing == null || _last != RenderSize)
        {
            _last = RenderSize;
            _drawing = ParticleTexture.Get(ParticleTexture.Form.Wing, 71, ActualWidth, ActualHeight);
        }
        dc.PushTransform(_drift); dc.DrawDrawing(_drawing); dc.Pop();
    }
}

// Keep native menu navigation and submenus while fading just the local highlight.
internal sealed class ParticleMenuItem : MenuItem
{
    private Border? _highlight;
    internal ParticleMenuItem()
    {
        Unloaded += (_, _) => { BeginAnimation(OpacityProperty, null); _highlight?.BeginAnimation(OpacityProperty, null); };
        SubmenuOpened += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, this)) return;
            int index = 0; foreach (var item in Items.OfType<MenuItem>()) Ui.Reveal(item, index++ * 12);
        };
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); _highlight = GetTemplateChild("PART_ParticleHighlight") as Border;
        if (_highlight != null) _highlight.Opacity = IsHighlighted || IsSubmenuOpen ? 1 : 0;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_highlight != null && (e.Property == IsHighlightedProperty || e.Property == IsSubmenuOpenProperty))
            Ui.Fade(_highlight, IsHighlighted || IsSubmenuOpen ? 1 : 0);
    }
}
