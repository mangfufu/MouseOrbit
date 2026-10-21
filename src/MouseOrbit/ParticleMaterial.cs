using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MouseOrbit;

internal static class ParticleTone
{
    private static readonly DependencyProperty ToneProperty = DependencyProperty.RegisterAttached("Tone", typeof(Color), typeof(ParticleTone),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    internal static void Watch(FrameworkElement view) => System.Windows.Data.BindingOperations.SetBinding(view, ToneProperty,
        new System.Windows.Data.Binding(nameof(SolidColorBrush.Color)) { Source = Palette.Ink });
}

// Protect lettering rather than placing a rectangular panel behind every row.
internal sealed class ParticleLabel : Decorator
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(ParticleLabel),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (sender, args) => ((ParticleLabel)sender)._text.Text = args.NewValue as string ?? ""));
    private readonly TextBlock _text = new() { FontFamily = Palette.BrandFont, FontSize = 13, Foreground = Palette.Ink, IsHitTestVisible = false };
    public ParticleLabel() { Child = _text; IsHitTestVisible = false; ParticleTone.Watch(this); }
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

internal sealed class ParticleIcon : FrameworkElement
{
    private readonly string _kind;
    internal ParticleIcon(string kind) { _kind = kind; ParticleTone.Watch(this); }
    protected override void OnRender(DrawingContext dc)
    {
        var icon = (DrawingImage)CommandIcons.Get(_kind);
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        if (icon.Drawing is DrawingGroup group)
            foreach (var drawing in group.Children.OfType<GeometryDrawing>().Where(d => d.Pen != null))
                dc.DrawGeometry(null, new Pen(Palette.Surface, 4.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, drawing.Geometry);
        dc.DrawDrawing(icon.Drawing); dc.Pop();
    }
}

// The same softly dispersed material on larger surfaces, without outlining a large rectangle.
public sealed class ParticlePanel : Border
{
    public ParticlePanel() { ParticleTone.Watch(this); }
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 20 || ActualHeight < 20) return;
        var color = (Background as SolidColorBrush)?.Color ?? Palette.Surface.Color;
        color.A = (byte)Math.Round((1 - Math.Pow(1 - color.A / 255d, 1d / 18)) * 255);
        var brush = new SolidColorBrush(color); brush.Freeze();
        for (int i = 0; i < 18; i++)
        {
            double inset = i * .5;
            dc.DrawRoundedRectangle(brush, null, new Rect(inset, inset, ActualWidth - inset * 2, ActualHeight - inset * 2), 20 - inset, 20 - inset);
        }
        dc.PushOpacity(.3); dc.PushTransform(new TranslateTransform(8, 8));
        LiquidPaint.Draw(dc, new FlowState(3), Math.Min(236, ActualWidth - 16), 60); dc.Pop(); dc.Pop();
    }
}

// A refractive flow surface; only the artwork deforms, never the button's input plane.
internal sealed class ParticleField : Grid
{
    private readonly LiquidFlow _liquid;
    private bool _revealing;
    private int _reveal;
    internal bool AmbientRunning => _liquid.Running;
    internal bool MotionRunning => AmbientRunning || _revealing;
    internal FlowState FlowState => _liquid.State;
    internal ParticleField(int seed, double width = 236, double height = 60)
    {
        Width = width; Height = height; HorizontalAlignment = HorizontalAlignment.Left; IsHitTestVisible = false;
        _liquid = new LiquidFlow(seed); Children.Add(_liquid);
        Unloaded += (_, _) => Stop();
    }
    internal void SetActive(bool active, bool animate = true)
    {
        _liquid.State.SetFocus(active, animate && _liquid.Running); _liquid.InvalidateVisual();
    }
    internal void SetJoined(bool connected)
    {
        _liquid.State.SetJoined(connected, _liquid.Running); _liquid.InvalidateVisual();
    }
    internal void SetPointer(Point? pointer) => _liquid.SetPointer(pointer);
    internal void Press(bool pressed) => _liquid.State.SetPress(pressed);
    internal void BindIcon(FrameworkElement icon)
    {
        var scale = new ScaleTransform(1, 1); var shift = new TranslateTransform();
        var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(shift);
        icon.RenderTransform = transform; icon.RenderTransformOrigin = new Point(.5, .5);
        _liquid.Advanced += () =>
        {
            double focus = SystemParameters.ClientAreaAnimation && _liquid.Running ? _liquid.State.Focus.Value : 0;
            double press = _liquid.Running ? _liquid.State.Press.Value : 0;
            scale.ScaleX = scale.ScaleY = 1 + focus * .055 - press * .06;
            shift.X = focus * 1.8;
        };
    }
    internal void Pulse(bool animate = true, int delay = 0)
    {
        int version = ++_reveal; BeginAnimation(OpacityProperty, null); Opacity = 1;
        _revealing = animate && SystemParameters.ClientAreaAnimation && IsLoaded && IsVisible;
        if (!_revealing) return;
        var reveal = new DoubleAnimation(.12, 1, TimeSpan.FromMilliseconds(210))
        { BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        reveal.Completed += (_, _) => { if (version == _reveal) { _revealing = false; BeginAnimation(OpacityProperty, null); } };
        BeginAnimation(OpacityProperty, reveal);
    }
    internal void SetPresented(bool presented) => _liquid.SetPresented(presented);
    internal void Stop()
    {
        ++_reveal; _revealing = false; BeginAnimation(OpacityProperty, null); Opacity = 1;
        _liquid.SetPresented(false); _liquid.State.Settle(); _liquid.InvalidateVisual();
    }
}

// Native menus keep their existing interaction, with a slow liquid membrane behind their content.
public sealed class ParticleMenuPlate : Border, IFlowFrame
{
    private readonly FlowState _state = new(71);
    private bool _running;
    public ParticleMenuPlate()
    {
        ParticleTone.Watch(this);
        Loaded += (_, _) => UpdateMotion(); Unloaded += (_, _) => StopMotion();
        IsVisibleChanged += (_, _) => UpdateMotion();
        MouseMove += (_, args) => _state.SetPointer(args.GetPosition(this));
        MouseLeave += (_, _) => _state.SetPointer(null);
    }
    private void UpdateMotion()
    {
        bool run = IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (run == _running) return;
        _running = run; if (run) FlowClock.Add(this); else StopMotion();
    }
    private void StopMotion() { _running = false; FlowClock.Remove(this); _state.Settle(); }
    public void AdvanceFlow(double seconds) { _state.Step(seconds); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc) => LiquidPaint.Draw(dc, _state, ActualWidth, ActualHeight, plate: true);
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
