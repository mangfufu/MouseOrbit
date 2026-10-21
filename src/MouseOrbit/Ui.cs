using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MouseOrbit;

// Shared material, typography, icons and motion for every WPF surface.
internal static class Ui
{
    internal static readonly Color[] AnnotationColors = new[] { "#E75B3D", "#E8B63A", "#39AE77", "#428BDC", "#222222", "#FFFFFF" }
        .Select(hex => (Color)ColorConverter.ConvertFromString(hex)).ToArray();
    internal static readonly double[] LineWidths = [2, 3, 5, 8, 12];
    internal static readonly double[] FontSizes = [12, 16, 20, 24, 32, 40, 56];
    internal static void Install(Window window)
    {
        Palette.Resources(window.Resources);
        if (!window.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("Controls.xaml") == true))
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MouseOrbit;component/Controls.xaml", UriKind.Relative) });
        window.FontFamily = Palette.BrandFont; window.FontSize = 12; window.Foreground = Palette.Ink;
        window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
        window.Icon = CommandIcons.Get("wheel");
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
    }
    internal static Border Shell(UIElement child, Thickness? padding = null, double radius = 10) => new SurfacePanel
    {
        Background = Palette.Surface, BorderBrush = Palette.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius),
        Padding = padding ?? new Thickness(8), Child = child, SnapsToDevicePixels = true
    };
    internal static StackPanel Label(string icon, string label, double size = 16)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new UiIcon { Kind = icon, Width = size, Height = size, Margin = new Thickness(0, 0, 7, 0) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Ink }); return row;
    }
    internal static Button Action(string icon, string label, Action action, string? tooltip = null)
        => Palette.Button(Label(icon, label), action, tooltip ?? label);
    internal static Button Icon(string icon, Action action, string tooltip, double size = 16)
    {
        var button = Palette.Button(new UiIcon { Kind = icon, Width = size, Height = size }, action, tooltip);
        button.Width = button.Height = 32; button.Padding = new Thickness(7); return button;
    }
    internal static ContextMenu Menu(FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Background = Palette.Surface, Foreground = Palette.Ink,
            BorderBrush = Palette.Edge, HasDropShadow = false, FontFamily = Palette.BrandFont, FontSize = 12 };
        Palette.Resources(menu.Resources);
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MouseOrbit;component/Controls.xaml", UriKind.Relative) });
        return menu;
    }
    internal static void Enter(FrameworkElement view, double distance = 6, Point? origin = null)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        view.RenderTransformOrigin = origin ?? new Point(.5, .5);
        var shift = new TranslateTransform(); var scale = new ScaleTransform(1, 1); var fold = new SkewTransform();
        var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(fold); transforms.Children.Add(shift); view.RenderTransform = transforms;
        DoubleAnimation Settle(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(340)) { FillBehavior = FillBehavior.Stop, EasingFunction = new FluidEase() };
        shift.BeginAnimation(TranslateTransform.YProperty, Settle(distance, 0));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Settle(.965, 1)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Settle(.94, 1));
        view.Opacity = 1; view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.15, 1, TimeSpan.FromMilliseconds(170)) { FillBehavior = FillBehavior.Stop });
    }
    internal static void HeightTo(FrameworkElement view, double target, bool expanding, int delay = 0, Action? completed = null)
        => FluidHeight.To(view, target, expanding, delay, completed);
    internal static void StopEntrance(FrameworkElement view)
    {
        view.BeginAnimation(UIElement.OpacityProperty, null); view.Opacity = 1;
        if (view.RenderTransform is not TransformGroup group) return;
        foreach (var transform in group.Children)
        {
            if (transform is TranslateTransform shift) { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null); }
            if (transform is ScaleTransform scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
            if (transform is SkewTransform skew) { skew.BeginAnimation(SkewTransform.AngleXProperty, null); skew.BeginAnimation(SkewTransform.AngleYProperty, null); }
        }
    }
    internal static void Reveal(UIElement view, int delay = 0)
    {
        view.BeginAnimation(UIElement.OpacityProperty, null); view.Opacity = 1;
        if (!SystemParameters.ClientAreaAnimation || !view.IsVisible) return;
        view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(170))
        { BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    internal static void Fade(UIElement view, double target)
    {
        double from = view.Opacity; view.BeginAnimation(UIElement.OpacityProperty, null); view.Opacity = target;
        if (!SystemParameters.ClientAreaAnimation || !view.IsVisible) return;
        view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(140))
        { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    internal static System.Drawing.Icon TrayIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var disk = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 91, 113, 122)); graphics.FillEllipse(disk, 1, 1, 30, 30);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(245, 241, 247, 249), 1.7f);
            graphics.DrawEllipse(pen, 8, 8, 16, 16);
            for (int i = 0; i < 8; i++) { double a = Math.PI * i / 4; graphics.DrawLine(pen, (float)(16 + 4 * Math.Cos(a)), (float)(16 + 4 * Math.Sin(a)), (float)(16 + 8 * Math.Cos(a)), (float)(16 + 8 * Math.Sin(a))); }
        }
        nint handle = bitmap.GetHicon();
        try { using var icon = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
}

internal sealed class FluidEase : EasingFunctionBase
{
    protected override double EaseInCore(double t) => t >= 1 ? 1 : 1 - Math.Exp(-8 * t) * (Math.Cos(9 * t) + 8d / 9 * Math.Sin(9 * t));
    protected override Freezable CreateInstanceCore() => new FluidEase();
    internal FluidEase() { EasingMode = EasingMode.EaseIn; }
}

// Quiet optical plane: two clipped corners and a short crease highlight.
// The child keeps its normal layout; the material never bends labels or hit targets.
public sealed class SurfacePanel : Border
{
    private Size _size;
    private double _cut;
    private StreamGeometry? _outline;
    protected override void OnRender(DrawingContext drawing)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) return;
        if (Palette.Spatial)
        {
            drawing.DrawRoundedRectangle(Background, BorderThickness.Left > 0 ? new Pen(BorderBrush, .5) : null,
                new Rect(.5, .5, w - 1, h - 1), Palette.Layout == AppearanceLayout.Typefield ? 6 : 9, Palette.Layout == AppearanceLayout.Typefield ? 6 : 9);
            return;
        }
        double cut = Math.Min(Math.Min(w / 4, h / 4), Math.Clamp(CornerRadius.TopLeft, 0, 10));
        if (_outline == null || _size != new Size(w, h) || _cut != cut)
        {
            _size = new Size(w, h); _cut = cut; _outline = new StreamGeometry();
            using (var path = _outline.Open())
            {
                path.BeginFigure(new Point(.5, .5), true, true);
                path.PolyLineTo(new Point[] { new(w - cut - .5, .5), new(w - .5, cut + .5), new(w - .5, h - .5), new(cut + .5, h - .5), new(.5, h - cut - .5) }, true, false);
            }
            _outline.Freeze();
        }
        drawing.DrawGeometry(Background, BorderThickness.Left > 0 ? new Pen(BorderBrush, BorderThickness.Left) : null, _outline);
        if (Background == null || BorderThickness.Left <= 0) return;
        drawing.DrawLine(new Pen(Palette.Hairline, .75), new Point(.5, .5), new Point(Math.Min(w - cut - 1, 30), .5));
        if (cut > 0) drawing.DrawLine(new Pen(Palette.Hairline, .75), new Point(w - cut - .5, .5), new Point(w - .5, cut + .5));
    }
}

public sealed class UiIcon : FrameworkElement
{
    private static readonly DependencyProperty ToneProperty = DependencyProperty.Register("Tone", typeof(Color), typeof(UiIcon),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(UiIcon),
        new FrameworkPropertyMetadata("window", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public UiIcon()
    {
        Width = Height = 18;
        System.Windows.Data.BindingOperations.SetBinding(this, ToneProperty, new System.Windows.Data.Binding(nameof(SolidColorBrush.Color)) { Source = Palette.Ink });
    }
    protected override void OnRender(DrawingContext drawing) => drawing.DrawImage(CommandIcons.Get(Kind), new Rect(0, 0, ActualWidth, ActualHeight));
}
