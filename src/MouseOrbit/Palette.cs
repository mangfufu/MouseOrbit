using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace MouseOrbit;

internal static class Palette
{
    // A bound color cannot be frozen when WPF seals a control template.
    // Every open view therefore follows a theme change without replacing its brushes.
    private sealed class Tone : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(Color), typeof(Tone));
        public Color Value { get => (Color)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }
    private static readonly Dictionary<SolidColorBrush, Tone> Tones = new();
    private static SolidColorBrush ThemeBrush(Color color)
    {
        var tone = new Tone { Value = color }; var brush = new SolidColorBrush();
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(Tone.Value)) { Source = tone });
        Tones.Add(brush, tone); return brush;
    }
    private static void Set(SolidColorBrush brush, Color color) => Tones[brush].Value = color;
    public static readonly SolidColorBrush Ink = ThemeBrush(Color.FromRgb(66, 59, 47));
    public static readonly SolidColorBrush Muted = ThemeBrush(Color.FromRgb(132, 120, 101));
    public static readonly SolidColorBrush Surface = ThemeBrush(Color.FromArgb(241, 239, 233, 219));
    public static readonly SolidColorBrush Paper = ThemeBrush(Color.FromRgb(239, 233, 219));
    public static readonly SolidColorBrush Hover = ThemeBrush(Color.FromArgb(45, 146, 125, 95));
    public static readonly SolidColorBrush Selected = ThemeBrush(Color.FromArgb(66, 172, 86, 49));
    public static readonly SolidColorBrush Edge = ThemeBrush(Color.FromArgb(140, 146, 131, 107));
    public static readonly SolidColorBrush Accent = ThemeBrush(Color.FromRgb(170, 77, 42));
    public static readonly SolidColorBrush Card = ThemeBrush(Color.FromArgb(80, 255, 255, 255));
    public static readonly SolidColorBrush Chrome = ThemeBrush(Color.FromArgb(45, 255, 255, 255));
    public static readonly SolidColorBrush Hairline = ThemeBrush(Color.FromArgb(70, 255, 255, 255));
    public static readonly FontFamily PixelFont = new(new Uri("pack://application:,,,/MouseOrbit;component/"), "./Assets/Fonts/#Tiny5");
    public static bool Dark { get; private set; }
    private static bool _backdropDark;
    public static ThemePreset Current { get; private set; } = ThemePresets.Find("typefield");
    public static bool Pixel => Current.Pixel;
    public static AppearanceLayout Layout => Current.Layout;
    public static bool Spatial => Layout != AppearanceLayout.Classic;
    public static CornerRadius PanelCorner => new(Current.Radius);
    public static CornerRadius ButtonCorner => new(6);
    public static FontFamily BrandFont => new("Segoe UI, Microsoft YaHei UI");
    public static event Action? Changed;
    public static void SetTheme(bool dark)
        { SetBackdropDark(dark); Apply("typefield"); }
    internal static void SetBackdropDark(bool dark)
    {
        if (_backdropDark == dark) return;
        _backdropDark = dark; Apply(Current.Id);
    }
    public static void Apply(string id)
    {
        Current = ThemePresets.Find(id);
        if (_backdropDark) Current = Current with { Dark = true,
            Surface = "#DC282D30",
            Paper = "#303638",
            Ink = "#E8ECEF", Muted = "#CCD2D6", Accent = "#A5AFB5" };
        Dark = Current.Dark;
        Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        Set(Ink, C(Current.Ink)); Set(Muted, C(Current.Muted)); Set(Paper, C(Current.Paper)); Set(Surface, C(Current.Surface)); Set(Accent, C(Current.Accent));
        var muted = C(Current.Muted); var accent = C(Current.Accent);
        Set(Hover, Color.FromArgb(Dark ? (byte)25 : (byte)18, muted.R, muted.G, muted.B));
        Set(Selected, Color.FromArgb(Dark ? (byte)46 : (byte)32, accent.R, accent.G, accent.B));
        Set(Edge, Color.FromArgb(Dark ? (byte)63 : (byte)48, muted.R, muted.G, muted.B));
        Set(Card, Dark ? Color.FromArgb(10, 255, 255, 255) : Color.FromArgb(60, 255, 255, 255));
        Set(Chrome, Dark ? Color.FromArgb(15, 0, 0, 0) : Color.FromArgb(42, 255, 255, 255));
        Set(Hairline, Spatial ? Colors.Transparent : Dark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255));
        CommandIcons.Clear(); Changed?.Invoke();
    }
    public static void Resources(ResourceDictionary resources)
    {
        resources["Ink"] = Ink; resources["Muted"] = Muted; resources["Paper"] = Paper; resources["Surface"] = Surface;
        resources["Hover"] = Hover; resources["Selected"] = Selected; resources["Edge"] = Edge; resources["Accent"] = Accent; resources["PixelFont"] = PixelFont;
        resources["BrandFont"] = BrandFont; resources["PanelCorner"] = PanelCorner; resources["ButtonCorner"] = ButtonCorner;
        resources["Card"] = Card; resources["Chrome"] = Chrome; resources["Hairline"] = Hairline;
    }
    public static Brush B(string value) { var brush = (Brush)new BrushConverter().ConvertFromString(value)!; brush.Freeze(); return brush; }

    private static readonly Dictionary<bool, ControlTemplate> ButtonTemplates = new();
    private static ControlTemplate ButtonTemplate()
    {
        if (ButtonTemplates.TryGetValue(Pixel, out var cached)) return cached;
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Surface";
        border.SetValue(Border.CornerRadiusProperty, ButtonCorner);
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new(RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "Surface")); template.Triggers.Add(hover);
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Selected, "Surface")); template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .35)); template.Triggers.Add(disabled);
        ButtonTemplates[Pixel] = template; return template;
    }
    public static Button Button(object content, Action action, string? name = null)
    {
        var button = new Button
        {
            Content = content, Foreground = Ink, Background = Brushes.Transparent,
            Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0), FontFamily = BrandFont, FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, Template = ButtonTemplate()
        };
        if (name != null) { button.ToolTip = name; System.Windows.Automation.AutomationProperties.SetName(button, name); }
        button.Click += (_, _) => action();
        return button;
    }
}

internal static class CommandIcons
{
    private static readonly Dictionary<string, ImageSource> Cache = new();
    internal static void Clear() => Cache.Clear();
    public static ImageSource Get(string id)
    {
        if (Cache.TryGetValue(id, out var icon)) return icon;
        var path = id switch
        {
            "wheel" => "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,3 V7 M12,17 V21 M3,12 H7 M17,12 H21 M6,6 L9,9 M15,15 L18,18 M6,18 L9,15 M15,9 L18,6",
            "assistant" => "M3,3 H9 V9 H3 Z M15,3 H21 V9 H15 Z M3,15 H9 V21 H3 Z M15,15 H21 V21 H15 Z",
            "taskView" or "switchApp" or "restore" or "window" => "M8,3 H21 V16 H18 M3,8 H16 V21 H3 Z",
            "explorer" or "folder" => "M3,7 V5 H10 L12,8 H21 V20 H3 Z M3,8 H21",
            "downloads" => "M12,3 V15 M7,10 L12,15 L17,10 M4,16 V21 H20 V16",
            "desktop" or "displaySettings" => "M3,4 H21 V17 H3 Z M12,17 V21 M8,21 H16",
            "desktopLeft" => "M8,3 H22 V17 H8 M2,12 H14 M6,8 L2,12 L6,16 M15,17 V21 M11,21 H19",
            "desktopRight" => "M2,3 H16 V17 H2 M10,12 H22 M18,8 L22,12 L18,16 M9,17 V21 M5,21 H13",
            "newDesktop" => "M3,3 H21 V18 H3 Z M12,6 V14 M8,10 H16 M12,18 V22 M8,22 H16",
            "screenshot" => "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16",
            "settings" or "soundSettings" => "M4,7 H20 M4,17 H20 M8,4 V10 M16,14 V20",
            "maximize" => "M4,10 V4 H10 M14,4 H20 V10 M20,14 V20 H14 M10,20 H4 V14",
            "minimize" => "M5,17 H19",
            "snapLeft" => "M3,4 H21 V20 H3 Z M12,4 V20 M6,8 H9 M6,12 H9 M6,16 H9",
            "snapRight" => "M3,4 H21 V20 H3 Z M12,4 V20 M15,8 H18 M15,12 H18 M15,16 H18",
            "centerWindow" => "M3,7 V3 H7 M17,3 H21 V7 M21,17 V21 H17 M7,21 H3 V17 M7,7 H17 V17 H7 Z",
            "nextMonitor" => "M2,5 H13 V16 H2 Z M7,16 V20 M4,20 H10 M16,9 H23 M19,5 L23,9 L19,13",
            "topmost" => "M8,3 H16 M9,3 V9 L6,14 H18 L15,9 V3 M12,14 V22",
            "volumeUp" => "M3,9 H7 L12,5 V19 L7,15 H3 Z M16,12 H22 M19,9 V15",
            "volumeDown" => "M3,9 H7 L12,5 V19 L7,15 H3 Z M16,12 H22",
            "mute" => "M3,9 H7 L12,5 V19 L7,15 H3 Z M17,9 L22,15 M22,9 L17,15",
            "playPause" => "M4,4 L13,12 L4,20 Z M17,5 V19 M21,5 V19",
            "nextTrack" => "M4,5 L15,12 L4,19 Z M19,5 V19",
            "previousTrack" => "M20,5 L9,12 L20,19 Z M5,5 V19",
            "recent" => "M4,6 A9,9 0 1 1 3,14 M3,3 V8 H8 M12,6 V12 L16,14",
            "closeTab" or "closeWindow" or "close" => "M6,6 L18,18 M18,6 L6,18",
            "copy" => "M9,3 H21 V15 H18 M3,9 H15 V21 H3 Z",
            "paste" => "M8,5 H4 V21 H20 V5 H16 M8,3 H16 V7 H8 Z",
            "cut" => "M3,3 L20,20 M21,3 L4,20 M5,13 A4,4 0 1 0 5,21 A4,4 0 1 0 5,13 M19,13 A4,4 0 1 0 19,21 A4,4 0 1 0 19,13",
            "undo" or "back" => "M10,5 L3,12 L10,19 M3,12 H21",
            "forward" => "M14,5 L21,12 L14,19 M3,12 H21",
            "top" => "M5,3 H19 M12,21 V7 M6,13 L12,7 L18,13",
            "bottom" => "M5,21 H19 M12,3 V17 M6,11 L12,17 L18,11",
            "newTab" => "M12,4 V20 M4,12 H20",
            "reopen" or "refresh" => "M4,8 A9,9 0 1 1 3,14 M3,3 V8 H8",
            "save" => "M4,3 H17 L21,7 V21 H3 V3 Z M7,3 V9 H16 V3 M7,21 V14 H17 V21",
            "taskManager" => "M4,20 V13 M10,20 V4 M16,20 V9 M22,20 V6",
            "selectAll" => "M3,3 H21 V21 H3 Z M7,12 L10,15 L17,8",
            "search" => "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 M15,15 L21,21",
            "chevronUp" => "M7,14 L12,9 L17,14",
            "chevronDown" => "M7,10 L12,15 L17,10",
            "chevronRight" => "M9,7 L14,12 L9,17",
            "more" => "M5,11 V13 M12,11 V13 M19,11 V13",
            "rectangle" => "M4,5 H20 V19 H4 Z",
            "ellipse" => "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3",
            "arrow" => "M5,19 L19,5 M9,5 H19 V15",
            "pen" => "M4,20 L5,14 L16,3 L21,8 L10,19 Z M13,6 L18,11 M5,14 L10,19",
            "text" => "M4,5 H20 M12,5 V20 M8,20 H16 M4,5 V8 M20,5 V8",
            "redo" => "M14,5 L21,12 L14,19 M21,12 H10 A6,6 0 0 0 4,18",
            "record" => "M12,5 A7,7 0 1 0 12,19 A7,7 0 1 0 12,5",
            "pause" => "M8,5 V19 M16,5 V19",
            "stop" => "M5,5 H19 V19 H5 Z",
            "play" => "M7,4 L20,12 L7,20 Z",
            "focus" => "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15 M9,12 H15",
            "star" => "M12,3 L15,9 L22,10 L17,15 L18,22 L12,18 L6,22 L7,15 L2,10 L9,9 Z",
            _ => "M5,3 H15 L20,8 V21 H5 Z M14,3 V9 H20 M8,13 H16 M8,17 H14"
        };
        var group = new DrawingGroup();
        // A fixed view box makes unrelated symbols share the same optical size.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var pen = new Pen(Palette.Ink.CloneCurrentValue(), 1.45) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(path)));
        var result = new DrawingImage(group); result.Freeze(); Cache[id] = result; return result;
    }
}
