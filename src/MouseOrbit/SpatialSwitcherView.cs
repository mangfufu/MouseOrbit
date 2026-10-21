using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MouseOrbit;

internal sealed record SwitcherState(string Category = "windows", int Page = 0, string? Expanded = null);

// Continuous application flow with inline window groups.
internal sealed class SpatialSwitcherView : Grid
{
    private readonly RailScrollViewer _flow = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
    private readonly StackPanel _lines = new();
    private readonly Canvas _actions = new();
    private readonly List<ParticleField> _fields = new();
    private Button? _menuRow;
    private double _menuContentY;
    private bool _menuHeld;
    private bool _presented;
    private readonly Button _more;
    private readonly Dictionary<string, FrameworkElement> _cache = new();
    private IReadOnlyList<SwitchGroup> _groups = [];
    private Dictionary<string, IReadOnlyList<DesktopEntry>> _entries = new();
    private string? _expanded;
    internal string Category { get; private set; } = "windows";
    internal SwitcherState State => new(Category, 0, _expanded);
    internal event Action<DesktopEntry>? Selected;
    internal event Action<string>? CategoryChanged;
    internal event Action? MenuRequested, DismissRequested;
    internal event Action<string>? MessageRequested;
    internal Func<string, DesktopEntry, bool>? RemoveRequested;
    internal const double RowHeight = 60;
    internal RailScrollViewer Flow => _flow;
    internal FrameworkElement MenuAnchor => _more;
    internal static readonly ControlTemplate BareButton = new(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
    private static readonly Brush InputPlane = new SolidColorBrush(Color.FromArgb(1, 240, 240, 237));

    internal SpatialSwitcherView(SwitcherState? state = null)
    {
        Background = InputPlane;
        MouseLeftButtonDown += (_, e) =>
        {
            for (var node = e.OriginalSource as DependencyObject; node != null && node != this; node = VisualTreeHelper.GetParent(node))
                if (node is Button) return;
            if (_expanded != null) Collapse(); else DismissRequested?.Invoke();
            e.Handled = true;
        };
        if (state != null) { Category = state.Category; _expanded = state.Expanded; }
        _flow.Content = _lines; _flow.Margin = new Thickness(8, 8, 8, 8); Children.Add(_flow);
        _more = Ui.Icon("chevronRight", () => MenuRequested?.Invoke(), "更多", 12); _more.Width = 28; _more.Height = 36;
        var menuTarget = new Grid { Width = 28, Height = 36, Background = Brushes.Transparent };
        menuTarget.Children.Add(new UiIcon { Kind = "chevronRight", Width = 12, Height = 12 });
        _more.Content = menuTarget; _more.Template = BareButton; _more.Padding = new Thickness(0); _more.Opacity = .6;
        Canvas.SetLeft(_more, 216); Canvas.SetTop(_more, 12); _actions.Children.Add(_more); Children.Add(_actions);
        _more.MouseEnter += (_, _) => { Ui.Fade(_more, 1); if (_menuRow != null) FocusRow(_menuRow); };
        _more.MouseLeave += (_, _) => Ui.Fade(_more, .6);
        _flow.ScrollChanged += (_, _) => { PositionMenu(); RefreshMotion(); };
        Loaded += (_, _) => RefreshMotion();
        Unloaded += (_, _) => StopMotion();
        MouseLeave += (_, _) => { if (!_menuHeld && _menuRow?.Content is Grid grid) grid.Children.OfType<ParticleField>().FirstOrDefault()?.SetActive(false); };
        Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
    }
    internal void SetData(IReadOnlyList<SwitchGroup> groups, Dictionary<string, IReadOnlyList<DesktopEntry>> entries)
    {
        _groups = groups; _entries = entries;
        if (_expanded != null && !_groups.Any(g => g.Key == _expanded)) _expanded = null;
        _cache.Clear(); Rebuild();
    }
    internal void SelectCategory(string category)
    {
        if (category is not ("windows" or "frequent" or "recent")) return;
        Category = category; _expanded = null; _cache.Clear(); Rebuild(); CategoryChanged?.Invoke(category);
    }
    private int Count => Category == "windows" ? _groups.Count : _entries.GetValueOrDefault(Category)?.Count ?? 0;
    private string CategoryName => Category == "windows" ? "窗口" : Category == "frequent" ? "常用" : "最近";
    internal void Scroll(int delta) => _flow.MoveBy(-delta / 120d * RowHeight);
    private void Rebuild()
    {
        _more.ToolTip = CategoryName + " · 分类与设置";
        RebuildFlow();
    }
    private FrameworkElement Cached(string key, Func<FrameworkElement> create)
    { if (!_cache.TryGetValue(key, out var tile)) _cache[key] = tile = create(); return tile; }
    private void RebuildFlow()
    {
        double offset = _flow.VerticalOffset; object? anchor = _menuRow?.Tag;
        bool presented = _presented; var previous = _fields.ToHashSet();
        StopMotion(); _presented = presented; foreach (var field in _fields) field.SetActive(false, false);
        _fields.Clear(); _menuRow = null; _lines.Children.Clear();
        for (int i = 0; i < Count; i++)
        {
            FrameworkElement row;
            if (Category == "windows")
            {
                var group = _groups[i]; row = Cached("app:" + group.Key, () => AppButton(group));
                if (row is Button app && app.Content is Grid content)
                {
                    var arrow = content.Children.OfType<UiIcon>().FirstOrDefault(icon => icon.Tag as string == "group-direction");
                    if (arrow != null) { arrow.RenderTransform = new RotateTransform(90); arrow.Visibility = _expanded == group.Key ? Visibility.Visible : Visibility.Collapsed; }
                }
                _lines.Children.Add(row);
                Track(row);
                if (_expanded == group.Key)
                {
                    foreach (var window in group.Windows) { var child = WindowButton(window); _lines.Children.Add(child); Track(child); }
                }
            }
            else { var entry = _entries[Category][i]; row = Cached(Category + ":" + entry.Id, () => EntryButton(entry)); _lines.Children.Add(row); Track(row); }
            row.Margin = new Thickness(0);
        }
        if (Count == 0)
        {
            var empty = Palette.Button("切换分类", () => MenuRequested?.Invoke(), "暂无项目，切换分类");
            empty.Margin = new Thickness(40, 0, 0, 0); empty.Height = RowHeight; empty.HorizontalAlignment = HorizontalAlignment.Left; _lines.Children.Add(empty);
        }
        _flow.Jump(offset);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _flow.Jump(Math.Min(offset, _flow.ScrollableHeight)); AnchorRow(anchor); RefreshMotion();
            if (!_presented) return;
            int index = 0;
            foreach (var field in VisibleFields().Where(f => !previous.Contains(f))) Reveal(field, index++ * 12);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void Track(FrameworkElement row)
    {
        var button = row as Button ?? (row as Grid)?.Children.OfType<Button>().FirstOrDefault();
        if (button?.Content is Grid grid && grid.Children.OfType<ParticleField>().FirstOrDefault() is { } field) _fields.Add(field);
    }
    private void AnchorRow(object? anchor)
    {
        var buttons = _lines.Children.OfType<FrameworkElement>().Select(row => row as Button ?? (row as Grid)?.Children.OfType<Button>().FirstOrDefault()).OfType<Button>().ToArray();
        var button = anchor == null ? buttons.FirstOrDefault() : buttons.FirstOrDefault(b => Equals(b.Tag, anchor)) ?? buttons.FirstOrDefault();
        if (button != null) FocusRow(button, false, false); else { _menuContentY = 0; PositionMenu(); }
    }
    internal void FocusRow(Button button, bool animate = true, bool active = true)
    {
        if (_menuRow != button && _menuRow?.Content is Grid previous) previous.Children.OfType<ParticleField>().FirstOrDefault()?.SetActive(false);
        _menuRow = button;
        if (button.Content is Grid grid && grid.Children.OfType<ParticleField>().FirstOrDefault() is { } field)
        { field.SetJoined(_menuHeld); field.SetActive(active, animate); }
        _menuContentY = button.TransformToAncestor(_lines).Transform(new Point()).Y;
        PositionMenu();
    }
    private void PositionMenu()
    {
        double top = 8 + _menuContentY - _flow.VerticalOffset;
        Canvas.SetTop(_more, top + (RowHeight - 36) / 2);
        _more.Visibility = top >= 7 && top + 80 <= (Height > 0 ? Height : 376) - 7 ? Visibility.Visible : Visibility.Hidden;
    }
    internal void HoldMenu(bool held)
    {
        _menuHeld = held;
        if (_menuRow?.Content is Grid grid && grid.Children.OfType<ParticleField>().FirstOrDefault() is { } field)
        { field.SetJoined(held); field.SetActive(held || IsMouseOver); }
    }
    internal void RevealParticles()
    {
        _presented = true; RefreshMotion();
        int index = 0; foreach (var field in VisibleFields()) Reveal(field, index++ * 12);
    }
    private ParticleField[] VisibleFields()
    {
        var viewport = new Rect(0, _flow.VerticalOffset, 254, Math.Max(0, _flow.ViewportHeight));
        return _fields.Where(f => f.IsVisible && f.RenderSize.Width > 0 && f.RenderSize.Height > 0 &&
            f.TransformToAncestor(_lines).TransformBounds(new Rect(f.RenderSize)).IntersectsWith(viewport)).Take(12).ToArray();
    }
    private void RefreshMotion()
    {
        if (!_presented) return;
        var visible = VisibleFields().ToHashSet();
        foreach (var field in _fields) field.SetPresented(visible.Contains(field));
    }
    private static void Reveal(ParticleField field, int delay)
    { field.Pulse(delay: delay); if (field.Parent is UIElement content) Ui.Reveal(content, delay); }
    private Button AppButton(SwitchGroup group)
    {
        var button = Choice(group.Recent.Icon, group.Name, () =>
        {
            if (group.Windows.Count == 1) { Pick(group.Recent); return; }
            bool collapse = _expanded == group.Key; _expanded = collapse ? null : group.Key;
            RebuildFlow();
            if (!collapse)
            {
                Dispatcher.BeginInvoke(new Action(() => RevealChildren(group.Key)), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }, 236, RowHeight, group.Windows.Count);
        button.Tag = group.Key; return button;
    }
    private void RevealChildren(string key)
    {
        if (_expanded != key) return;
        var children = _lines.Children.OfType<FrameworkElement>().Where(e => e.Tag is SwitchWindow).ToArray();
        if (children.Length == 0) return;
        var first = children[0].TransformToAncestor(_lines).Transform(new Point());
        double bottom = first.Y + Math.Min(children.Sum(c => c.ActualHeight), _flow.ViewportHeight - RowHeight);
        if (bottom > _flow.VerticalOffset + _flow.ViewportHeight) _flow.Jump(bottom - _flow.ViewportHeight);
    }
    private Button WindowButton(SwitchWindow window)
    {
        var button = Choice(window.Icon, window.Title, () => Pick(window), 220, 40);
        if (button.Content is Grid content)
        {
            var icon = content.Children.OfType<EntryIcon>().Single(); icon.Width = icon.Height = 18; icon.Margin = new Thickness(11, 0, 0, 0);
        }
        button.Margin = new Thickness(16, 0, 0, 0);
        button.Tag = window; return button;
    }
    private void Pick(SwitchWindow window)
    {
        WindowNative.GetWindowThreadProcessId(window.Handle, out uint pid);
        if (!WindowNative.IsWindow(window.Handle) || pid != window.Process) { MessageRequested?.Invoke("这个窗口已经关闭"); return; }
        Selected?.Invoke(window.Entry);
    }
    private FrameworkElement EntryButton(DesktopEntry entry)
    {
        var tile = new Grid { Width = 236, Height = RowHeight };
        var choice = Choice(entry, entry.Name, () => Selected?.Invoke(entry), 236, RowHeight); choice.Tag = entry.Id; tile.Children.Add(choice);
        var remove = Ui.Icon("close", () =>
        {
            if (RemoveRequested?.Invoke(Category, entry) != true) { MessageRequested?.Invoke("移除失败，请重试"); return; }
            _entries[Category] = (_entries.GetValueOrDefault(Category) ?? []).Where(e => e.Id != entry.Id).ToArray(); _cache.Remove(Category + ":" + entry.Id); Rebuild();
        }, (Category == "recent" ? "从最近移除：" : "从常用移除：") + entry.Name, 10);
        remove.Width = remove.Height = 28; remove.Margin = new Thickness(0, 0, 28, 0); remove.Padding = new Thickness(8); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Center;
        remove.Opacity = .15; tile.MouseEnter += (_, _) => Ui.Fade(remove, .85); tile.MouseLeave += (_, _) => Ui.Fade(remove, .15);
        tile.Children.Add(remove); return tile;
    }
    private Button Choice(DesktopEntry entry, string name, Action action, double width, double height, int count = 0)
    {
        var content = new Grid { Width = width, Height = height, Background = Brushes.Transparent };
        int seed = 23; foreach (char letter in entry.Id) seed = unchecked(seed * 31 + letter);
        var surface = new ParticleField(seed, width, height);
        content.Children.Add(surface);
        // Native artwork floats freely. Monochrome fallback symbols retain a small contrast shield.
        if (entry.Kind == "command") content.Children.Add(new Border { Width = 32, Height = 32,
            Background = Palette.Surface, CornerRadius = new CornerRadius(6), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) });
        content.Children.Add(new EntryIcon { Entry = entry, Width = 32, Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(7, 0, 0, 0) });
        content.Children.Add(new ParticleLabel { Text = name, Foreground = Palette.Ink, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(60, 0, count > 1 || Category != "windows" ? 68 : 32, 0) });
        if (count > 1)
        {
            var text = new ParticleLabel { Text = count.ToString(), Foreground = Palette.Ink, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 50, 0),
                TextAlignment = TextAlignment.Right, Width = 22 };
            content.Children.Add(text);
            content.Children.Add(new UiIcon { Kind = "chevronRight", Tag = "group-direction", Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 34, 0), RenderTransformOrigin = new Point(.5, .5) });
        }
        var button = Palette.Button(content, action, name); button.Template = BareButton; button.Width = width; button.Height = height; button.Padding = new Thickness(0); button.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTipService.SetInitialShowDelay(button, 500);
        button.MouseEnter += (_, _) => FocusRow(button);
        button.PreviewMouseLeftButtonDown += (_, _) => surface.SetActive(true);
        return button;
    }
    internal void Collapse() { if (_expanded == null) return; _expanded = null; Rebuild(); }
    internal void StopMotion()
    {
        _presented = false; _flow.StopMotion();
        _more.BeginAnimation(OpacityProperty, null);
        foreach (var field in _fields)
        {
            field.Stop();
            if (field.Parent is UIElement content) { content.BeginAnimation(OpacityProperty, null); content.Opacity = 1; }
        }
    }
}
