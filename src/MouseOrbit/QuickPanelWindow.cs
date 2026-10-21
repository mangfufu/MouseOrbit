using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MouseOrbit;

internal sealed class QuickPanelWindow : Window
{
    internal const double ItemHeight = 56;
    private readonly Dictionary<string, RailScrollViewer> _pages = new();
    private Dictionary<string, IReadOnlyList<DesktopEntry>> _snapshot = new();
    private IReadOnlyList<SwitchGroup> _windows = [];
    private SwitchGroup? _expanded;
    private string _group = "windows";
    private Grid _rows = new(), _navigation = new();
    private Border _shell = new();
    private RailScrollViewer? _currentPage;
    internal SpatialSwitcherView? SpatialView { get; private set; }
    private Button _back = null!, _up = null!, _down = null!, _category = null!;
    private ContextMenu? _menu;
    private Rect _bounds;
    private Native.Point _anchor;
    private bool _shown, _leftSide;
    private sealed record WindowTileData(SwitchWindow Window, SwitchGroup? App);
    private string PageKey => _expanded == null ? _group : "app:" + _expanded.Key;
    public new bool IsVisible => _shown;
    internal event Action? OpenStateChanged;
    public event Action<DesktopEntry>? Selected;
    public event Action? Manage;
    public event Action? Dismissed;
    public event Action<string>? ToolRequested;
    public Func<DesktopEntry, bool>? RemoveRecentRequested;
    public Func<DesktopEntry, bool>? RemoveFrequentRequested;
    internal (double Data, double Screen, double Layout, double Show) LastOpenTimings { get; private set; }

    public QuickPanelWindow()
    {
        Title = "MouseOrbit"; Width = 86; Height = 376;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        UseLayoutRounding = true; SnapsToDevicePixels = true; FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        Ui.Install(this); BuildLayout();
        SourceInitialized += (_, _) => { Native.OverlayStyle(this, false); Native.DisableTransitions(this); };
        Palette.Changed += ThemeChanged;
        Closed += (_, _) => { SpatialView?.StopMotion(); Ui.StopEntrance(_shell); _shown = false; OpenStateChanged?.Invoke(); Palette.Changed -= ThemeChanged; if (_menu != null) _menu.IsOpen = false; };
        PreviewMouseWheel += (_, e) => { ScrollWheel(e.Delta); e.Handled = true; };
    }
    private void ThemeChanged()
    {
        double offset = _currentPage?.VerticalOffset ?? 0;
        if (_menu != null) _menu.IsOpen = false;
        BuildLayout(SpatialView?.State);
        if (_shown) { var screen = FitToScreen(_anchor); Place(_anchor, screen); }
        LayoutContent(); _currentPage?.Jump(offset); UpdateScrollEdges();
        if (_shown) SpatialView?.RevealParticles();
    }
    internal static Size PreferredSize => Palette.Layout switch {
        AppearanceLayout.Typefield => new(254, 376), _ => new(86, 376)
    };
    private static Image Symbol(string id, double size = 14) => new() { Source = CommandIcons.Get(id), Width = size, Height = size, Opacity = .8 };
    private void BuildLayout(SwitcherState? state = null)
    {
        SpatialView?.StopMotion();
        _pages.Clear(); _currentPage = null;
        Width = PreferredSize.Width; Height = PreferredSize.Height;
        if (Palette.Spatial)
        {
            SpatialView = new SpatialSwitcherView(state ?? new SwitcherState(_group, 0, _expanded?.Key));
            SpatialView.Width = PreferredSize.Width; SpatialView.Height = PreferredSize.Height;
            _shell = Ui.Shell(new Viewbox { Child = SpatialView, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }, new Thickness(0)); _shell.Background = null; _shell.BorderThickness = new Thickness(0);
            Content = _shell;
            SpatialView.Selected += entry => { Hide(); Selected?.Invoke(entry); };
            SpatialView.CategoryChanged += value => _group = value;
            SpatialView.MenuRequested += OpenMenu;
            SpatialView.DismissRequested += () => { Hide(); Dismissed?.Invoke(); };
            SpatialView.RemoveRequested = (group, entry) => (group == "recent" ? RemoveRecentRequested : RemoveFrequentRequested)?.Invoke(entry) == true;
            SpatialView.MessageRequested += Message;
            SpatialView.SetData(_windows, _snapshot); return;
        }
        if (state != null) { _group = state.Category; _expanded = _windows.FirstOrDefault(g => g.Key == state.Expanded); }
        SpatialView = null;
        _shell = Ui.Shell(new Grid(), new Thickness(5)); _shell.Margin = new Thickness(4); _shell.RenderTransformOrigin = new Point(.5, .5);
        if (Palette.Spatial) { _shell.Background = null; _shell.BorderThickness = new Thickness(0); }
        Content = _shell;
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new GridLength(28) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new GridLength(28) }); _shell.Child = layout;
        var header = new Grid(); layout.Children.Add(header);
        _navigation = new Grid(); header.Children.Add(_navigation);
        _category = Palette.Button("窗口", OpenCategories, "切换列表"); _category.FontSize = 11; _category.Padding = new Thickness(2);
        _category.Margin = new Thickness(1, 0, 1, 0);
        if (Palette.Spatial) { _category.HorizontalAlignment = HorizontalAlignment.Left; _category.Background = Palette.Surface; _category.Padding = new Thickness(8, 3, 8, 3); }
        _navigation.Children.Add(_category);
        _back = Palette.Button(Symbol("back"), () => { _expanded = null; SelectGroup("windows"); }, "返回应用");
        _back.Height = 24; _back.Margin = new Thickness(1, 0, 1, 0); _back.Visibility = Visibility.Collapsed; header.Children.Add(_back);
        if (Palette.Spatial) { _back.HorizontalAlignment = HorizontalAlignment.Left; _back.Background = Palette.Surface; }
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = new GridLength(12) }); body.RowDefinitions.Add(new()); body.RowDefinitions.Add(new() { Height = new GridLength(12) });
        Grid.SetRow(body, 1); layout.Children.Add(body);
        _up = Palette.Button(Symbol("chevronUp", 12), () => _currentPage?.MoveBy(-ScrollStep), "向上滚动"); _up.Padding = new Thickness(0); body.Children.Add(_up);
        _rows = new Grid { ClipToBounds = true }; Grid.SetRow(_rows, 1); body.Children.Add(_rows);
        _down = Palette.Button(Symbol("chevronDown", 12), () => _currentPage?.MoveBy(ScrollStep), "向下滚动"); _down.Padding = new Thickness(0); Grid.SetRow(_down, 2); body.Children.Add(_down);
        var footer = new UniformGrid { Columns = 2 };
        var footerShell = new Border { Child = footer, BorderBrush = Palette.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(1), Margin = new Thickness(3, 1, 3, 0) }; Grid.SetRow(footerShell, 2); layout.Children.Add(footerShell);
        if (Palette.Spatial) { footerShell.BorderThickness = new Thickness(0); footerShell.Background = Palette.Surface; footerShell.CornerRadius = new CornerRadius(6); footerShell.Width = 74; footerShell.HorizontalAlignment = HorizontalAlignment.Left; }
        var capture = Palette.Button(Symbol("screenshot"), () => { Hide(); ToolRequested?.Invoke("capture"); }, "截图"); capture.Padding = new Thickness(3); footer.Children.Add(capture);
        var more = Palette.Button(Symbol("settings", 14), OpenMenu, "更多"); more.Padding = new Thickness(3); footer.Children.Add(more);
        SelectGroup(_group, false);
    }
    private void OpenMenu()
    {
        if (_menu != null) _menu.IsOpen = false;
        _menu = CreateMenu(); SpatialView?.HoldMenu(true);
        _menu.Closed += (_, _) => SpatialView?.HoldMenu(false);
        _menu.IsOpen = true;
    }
    internal void SelectCategory(string group)
    { if (SpatialView != null) { SpatialView.SelectCategory(group); return; } if (group is "windows" or "frequent" or "recent") { _expanded = null; SelectGroup(group); } }
    private void OpenCategories() => OpenCategoriesFor(_category);
    private void OpenCategoriesFor(FrameworkElement target)
    {
        _menu = Ui.Menu(target);
        foreach (var (id, name, icon) in new[] { ("windows", "窗口", "window"), ("frequent", "常用", "star"), ("recent", "最近", "recent") })
        {
            var item = new MenuItem { Header = name, Icon = Symbol(icon), IsChecked = id == _group };
            item.Click += (_, _) => SelectCategory(id); _menu.Items.Add(item);
        }
        _menu.Placement = _leftSide ? PlacementMode.Left : PlacementMode.Right; _menu.IsOpen = true;
    }
    internal ContextMenu CreateMenu()
    {
        var menu = Ui.Menu(SpatialView?.MenuAnchor ?? _shell); menu.Placement = _leftSide ? PlacementMode.Left : PlacementMode.Right;
        if (SpatialView != null)
        {
            var wing = new FrameworkElementFactory(typeof(ParticleMenuPlate));
            wing.SetValue(Border.PaddingProperty, new Thickness(10)); wing.SetValue(FrameworkElement.MinWidthProperty, 164d);
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter))); wing.AppendChild(scroll);
            menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = wing };
            menu.VerticalOffset = -32;
            menu.Opened += (_, _) => { Ui.Enter(menu, 3); int index = 0; foreach (var item in menu.Items.OfType<MenuItem>()) Ui.Reveal(item, index++ * 12); };
            menu.Closed += (_, _) => { Ui.StopEntrance(menu); foreach (var item in menu.Items.OfType<MenuItem>()) item.BeginAnimation(OpacityProperty, null); };
        }
        MenuItem Add(string name, Action action, string? icon = null, ItemsControl? parent = null)
        {
            MenuItem item = SpatialView != null ? new ParticleMenuItem() : new MenuItem();
            item.Header = name; item.Icon = icon == null ? null : Symbol(icon);
            if (SpatialView != null)
            {
                item.Style = menu.FindResource(typeof(MenuItem)) as Style;
                item.Template = (ControlTemplate)menu.FindResource("ParticleMenuItemTemplate");
                item.Padding = new Thickness(9, 10, 9, 10);
                var label = new FrameworkElementFactory(typeof(ParticleLabel));
                label.SetBinding(ParticleLabel.TextProperty, new System.Windows.Data.Binding());
                item.HeaderTemplate = new DataTemplate { VisualTree = label };
                if (icon != null) item.Icon = new ParticleIcon(icon) { Width = 15, Height = 15 };
            }
            if (SpatialView != null && name is "窗口" or "常用" or "最近")
                item.IsChecked = SpatialView.Category == (name == "窗口" ? "windows" : name == "常用" ? "frequent" : "recent");
            item.Click += (_, args) => { if (ReferenceEquals(args.OriginalSource, item)) action(); };
            (parent ?? menu).Items.Add(item); return item;
        }
        Add("窗口", () => { _expanded = null; SelectGroup("windows"); }, "window");
        Add("常用", () => { _expanded = null; SelectGroup("frequent"); }, "topmost");
        Add("最近", () => { _expanded = null; SelectGroup("recent"); }, "recent");
        ItemsControl settings = menu;
        if (SpatialView != null)
        {
            Add("截图 / GIF", () => { Hide(); ToolRequested?.Invoke("capture"); }, "screenshot");
            settings = Add("设置", () => { }, "settings");
        }
        else menu.Items.Add(new Separator());
        Add("轮盘", () => { Hide(); ToolRequested?.Invoke("wheel"); }, "wheel", settings);
        Add("专注模式", () => { Hide(); ToolRequested?.Invoke("focus"); }, parent: settings);
        Add("管理", () => { Hide(); Manage?.Invoke(); }, "settings", settings);
        Add("关闭", () => { Hide(); Dismissed?.Invoke(); }, "close", settings);
        return menu;
    }
    public void Message(string text) { _shell.ToolTip = text; }
    private void SelectGroup(string group, bool animate = true)
    {
        if (SpatialView != null) { SpatialView.SelectCategory(group); return; }
        _group = group;
        var category = Ui.Label(group == "windows" ? "window" : group == "frequent" ? "star" : "recent", group == "windows" ? "窗口" : group == "frequent" ? "常用" : "最近", 12);
        category.Children.Add(new UiIcon { Kind = "chevronDown", Width = 10, Height = 10, Margin = new Thickness(4, 0, 0, 0) });
        _category.Content = category;
        _back.Visibility = _expanded != null ? Visibility.Visible : Visibility.Collapsed;
        _navigation.Visibility = _expanded == null ? Visibility.Visible : Visibility.Collapsed;
        if (!_pages.TryGetValue(PageKey, out var page))
        {
            page = new RailScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false, PanningMode = PanningMode.VerticalOnly, Content = new StackPanel(), Visibility = Visibility.Hidden };
            _pages[PageKey] = page; _rows.Children.Add(page);
            page.ScrollChanged += (_, _) => { if (_currentPage == page) UpdateScrollEdges(); };
            if (group == "windows")
            {
                if (_expanded != null) PatchPage(page, _expanded.Windows, w => "window:" + w.Handle,
                    (tile, window) => tile.Tag is WindowTileData data && SameWindow(data.Window, window), w => WindowTile(w, null), "暂无窗口");
                else PatchPage(page, _windows, app => app.Key,
                    (tile, app) => tile.Tag is WindowTileData { App: { } old } && SameGroup(old, app), app => WindowTile(app.Recent, app), "暂无窗口");
            }
            else PatchEntries(page, _snapshot.GetValueOrDefault(group) ?? [], group);
        }
        if (_currentPage == page) return;
        _currentPage?.StopMotion();
        if (_currentPage != null) _currentPage.Visibility = Visibility.Hidden;
        _currentPage = page; page.Visibility = Visibility.Visible;
        UpdateScrollEdges();
        if (animate && _shown) AnimatePage(page);
    }
    private void UpdateScrollEdges()
    {
        if (_currentPage == null) return;
        bool above = _currentPage.VerticalOffset > .5, below = _currentPage.VerticalOffset < _currentPage.ScrollableHeight - .5;
        _up.IsEnabled = above; _up.Opacity = above ? .65 : .12;
        _down.IsEnabled = below; _down.Opacity = below ? .65 : .12;
        _currentPage.OpacityMask = new LinearGradientBrush(new GradientStopCollection {
            new(above ? Colors.Transparent : Colors.Black, 0), new(Colors.Black, .055), new(Colors.Black, .945), new(below ? Colors.Transparent : Colors.Black, 1)
        }, new Point(0, 0), new Point(0, 1));
    }
    private static void AnimatePage(FrameworkElement page)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var shift = new TranslateTransform(); page.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.YProperty, Motion(7, 0, 130)); page.BeginAnimation(OpacityProperty, Motion(.35, 1, 100));
    }
    private static DoubleAnimation Motion(double from, double to, double milliseconds) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
    internal double ScrollStep => ItemHeight;
    internal void ScrollWheel(int delta) { if (SpatialView != null) SpatialView.Scroll(delta); else _currentPage?.MoveBy(-delta / 120d * ScrollStep); }
    private static bool SameEntry(DesktopEntry first, DesktopEntry second) => first.Id == second.Id && first.Name == second.Name && first.Kind == second.Kind && first.Target == second.Target;
    private static bool SameWindow(SwitchWindow first, SwitchWindow second) => first.Handle == second.Handle && first.Process == second.Process &&
        first.Title == second.Title && first.Minimized == second.Minimized && first.Screen == second.Screen && first.IconEntry == second.IconEntry;
    private static bool SameGroup(SwitchGroup first, SwitchGroup second) => first.Key == second.Key && first.Name == second.Name && first.Windows.Count == second.Windows.Count &&
        first.Windows.Zip(second.Windows).All(pair => SameWindow(pair.First, pair.Second));
    private static string TileKey(FrameworkElement tile) => tile.Tag switch {
        DesktopEntry entry => entry.Id, WindowTileData data => data.App?.Key ?? "window:" + data.Window.Handle, _ => ""
    };
    private void PatchPage<T>(RailScrollViewer page, IReadOnlyList<T> items, Func<T, string> key,
        Func<FrameworkElement, T, bool> same, Func<T, FrameworkElement> create, string emptyMessage)
    {
        var stack = page.Content as StackPanel ?? new StackPanel(); page.Content = stack;
        var old = stack.Children.OfType<FrameworkElement>().ToLookup(TileKey);
        var wanted = items.Select(item => old[key(item)].FirstOrDefault(tile => same(tile, item)) ?? create(item)).ToArray();
        if (wanted.Length > 0 && stack.Children.OfType<FrameworkElement>().SequenceEqual(wanted)) return;
        var retained = wanted.ToHashSet();
        foreach (var tile in stack.Children.OfType<FrameworkElement>().Where(tile => !retained.Contains(tile)).ToArray()) stack.Children.Remove(tile);
        for (int i = 0; i < wanted.Length; i++)
        {
            if (i < stack.Children.Count && stack.Children[i] == wanted[i]) continue;
            stack.Children.Remove(wanted[i]); stack.Children.Insert(i, wanted[i]);
        }
        if (items.Count == 0) stack.Children.Add(new TextBlock { Text = emptyMessage, FontSize = 10, Foreground = Palette.Muted,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 22, 8, 22) });
    }
    private void PatchEntries(RailScrollViewer page, IReadOnlyList<DesktopEntry> entries, string group) => PatchPage(page, entries, e => e.Id,
        (tile, entry) => tile.Tag is DesktopEntry old && SameEntry(old, entry), entry => EntryTile(entry, group), "暂无项目");
    private void SetSnapshot(Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot)
    {
        foreach (string group in new[] { "frequent", "recent" })
        {
            var next = snapshot.GetValueOrDefault(group) ?? []; var old = _snapshot.GetValueOrDefault(group) ?? [];
            if (_pages.TryGetValue(group, out var page) && (old.Count != next.Count || !old.Zip(next).All(p => SameEntry(p.First, p.Second)))) PatchEntries(page, next, group);
        }
        _snapshot = snapshot.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DesktopEntry>)pair.Value.ToArray());
        SpatialView?.SetData(_windows, _snapshot);
    }
    private Button IconButton(DesktopEntry entry, string label, string tooltip, Action action, int count = 0, bool caption = false)
    {
        var content = new Grid { Width = 60, Height = ItemHeight - 2 };
        var icon = new EntryIcon { Entry = entry, Width = caption ? 26 : 32, Height = caption ? 26 : 32, VerticalAlignment = caption ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = caption ? new Thickness(0, 3, 0, 0) : new Thickness(0) }; content.Children.Add(icon);
        if (caption) content.Children.Add(new TextBlock { Text = label, Foreground = Palette.Ink, FontSize = 10, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 3) });
        var locator = new Border { Background = Palette.Accent, Width = 2, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 }; content.Children.Add(locator);
        if (count > 1) content.Children.Add(new Border { Background = Palette.Paper, CornerRadius = new CornerRadius(6), BorderBrush = Palette.Edge, BorderThickness = new Thickness(.5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 3, 0), Padding = new Thickness(3, 0, 3, 0),
            Child = new TextBlock { Text = count.ToString(), FontSize = 9, Foreground = Palette.Ink } });
        var button = Palette.Button(content, action, tooltip); button.Height = ItemHeight; button.Padding = new Thickness(0); button.Margin = new Thickness(1, 0, 1, 0);
        ToolTipService.SetInitialShowDelay(button, 300); ToolTipService.SetShowDuration(button, 10000);
        var scale = new ScaleTransform(1, 1); icon.RenderTransformOrigin = new Point(.5, .5); icon.RenderTransform = scale;
        void Hover(double to)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(scale.ScaleX, to, 120)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion(scale.ScaleY, to, 120));
        }
        button.MouseEnter += (_, _) => { Hover(1.08); locator.Opacity = .85; }; button.MouseLeave += (_, _) => { Hover(1); locator.Opacity = 0; };
        return button;
    }
    private FrameworkElement EntryTile(DesktopEntry entry, string group)
    {
        var tile = new Grid { Tag = entry }; tile.Children.Add(IconButton(entry, entry.ShortName, entry.Name, () => { Hide(); Selected?.Invoke(entry); }, caption: Palette.Spatial || entry.Kind is not ("app" or "command")));
        var remove = Palette.Button("×", () =>
        {
            var handler = group == "recent" ? RemoveRecentRequested : RemoveFrequentRequested;
            if (handler?.Invoke(entry) != true) { Message("移除失败，请重试"); return; }
            double offset = _currentPage?.VerticalOffset ?? 0;
            _snapshot[group] = (_snapshot.GetValueOrDefault(group) ?? []).Where(e => e.Id != entry.Id).ToArray();
            if (_currentPage != null) { PatchEntries(_currentPage, _snapshot[group], group); _currentPage.Jump(offset); }
        }, (group == "recent" ? "从最近移除：" : "从常用移除：") + entry.Name);
        remove.Width = remove.Height = 16; remove.FontSize = 11; remove.Padding = new Thickness(0); remove.Background = Palette.Surface;
        remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top; remove.Opacity = 0;
        tile.MouseEnter += (_, _) => remove.Opacity = 1; tile.MouseLeave += (_, _) => remove.Opacity = 0;
        tile.Children.Add(remove); return tile;
    }
    private FrameworkElement WindowTile(SwitchWindow window, SwitchGroup? app)
    {
        var button = IconButton(window.Icon, app?.Name ?? window.Title, app?.Name ?? window.Title, () =>
        {
            if (app?.Windows.Count > 1) { _expanded = app; SelectGroup("windows"); return; }
            WindowNative.GetWindowThreadProcessId(window.Handle, out uint pid);
            if (!WindowNative.IsWindow(window.Handle) || pid != window.Process) { Message("这个窗口已经关闭"); return; }
            Hide(); Selected?.Invoke(window.Entry);
        }, app?.Windows.Count ?? 0, caption: app == null);
        button.Tag = new WindowTileData(window, app); return button;
    }
    public bool Contains(Native.Point point) => IsVisible && (_bounds.Contains(point.X, point.Y) ||
        (_menu?.IsOpen == true && WindowNative.IsOwn(WindowNative.GetAncestor(WindowNative.WindowFromPoint(point), 2))));
    internal void SetEntries(Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot) { SetSnapshot(snapshot); _expanded = null; SelectGroup("windows", false); }
    internal void SetWindows(IReadOnlyList<SwitchGroup> groups)
    {
        groups = groups.Where(g => g.Windows.Count > 0).Select(g => g with { Windows = g.Windows.ToArray() }).ToArray();
        bool same = _windows.Count == groups.Count && _windows.Zip(groups).All(p => SameGroup(p.First, p.Second)); _windows = groups;
        if (same) return;
        if (SpatialView != null) { SpatialView.SetData(_windows, _snapshot); return; }
        if (_pages.TryGetValue("windows", out var page)) PatchPage(page, groups, g => g.Key,
            (tile, group) => tile.Tag is WindowTileData { App: { } previous } && SameGroup(previous, group), group => WindowTile(group.Recent, group), "暂无窗口");
        foreach (string key in _pages.Keys.Where(k => k.StartsWith("app:", StringComparison.Ordinal)).ToArray())
        {
            var app = groups.FirstOrDefault(g => "app:" + g.Key == key);
            if (app == null) { var removed = _pages[key]; _rows.Children.Remove(removed); _pages.Remove(key); if (_currentPage == removed) _currentPage = null; }
            else PatchPage(_pages[key], app.Windows, w => "window:" + w.Handle,
                (tile, window) => tile.Tag is WindowTileData previous && SameWindow(previous.Window, window), w => WindowTile(w, null), "暂无窗口");
        }
        if (_expanded != null) _expanded = groups.FirstOrDefault(g => g.Key == _expanded.Key);
        SelectGroup(_group, false);
    }
    internal void Prepare(Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot)
    {
        if (_shown) return;
        Native.GetCursorPos(out var point); FitToScreen(point); SetSnapshot(snapshot); PreparePages();
        if (!base.IsVisible) { Native.ClipPopup(this, true); base.Show(); }
    }
    private void PreparePages()
    {
        if (SpatialView != null) { LayoutContent(); return; }
        string selected = _group; var expanded = _expanded;
        foreach (string group in new[] { "frequent", "recent", "windows" }) { _expanded = null; SelectGroup(group, false); LayoutContent(); }
        foreach (var app in _windows.Where(g => g.Windows.Count > 1)) { _expanded = app; SelectGroup("windows", false); LayoutContent(); }
        _expanded = expanded; SelectGroup(selected, false); LayoutContent();
    }
    private void LayoutContent() { var content = (FrameworkElement)Content; content.Measure(new Size(Width, Height)); content.Arrange(new Rect(0, 0, Width, Height)); content.UpdateLayout(); }
    public void Open(Native.Point point, Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot)
    {
        if (!_shown) BackdropTone.Adapt(point);
        _anchor = point;
        long start = System.Diagnostics.Stopwatch.GetTimestamp(); SetSnapshot(snapshot);
        if (SpatialView == null) { _expanded = null; SelectGroup("windows", false); }
        long data = System.Diagnostics.Stopwatch.GetTimestamp();
        var screen = FitToScreen(point); double dpi = Math.Max(1, Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle) / 96d);
        long screenReady = System.Diagnostics.Stopwatch.GetTimestamp();
        _currentPage?.Jump(0); LayoutContent(); UpdateScrollEdges(); long layout = System.Diagnostics.Stopwatch.GetTimestamp();
        Place(point, screen);
        if (!base.IsVisible) { Native.ClipPopup(this, true); base.Show(); }
        _shell.BeginAnimation(OpacityProperty, null); _shell.Opacity = 1;
        Ui.Enter(_shell, 3);
        Native.ClipPopup(this, false); _shown = true;
        SpatialView?.RevealParticles();
        Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, new nint(-1), 0, 0, 0, 0, 0x0213); OpenStateChanged?.Invoke();
        LastOpenTimings = (System.Diagnostics.Stopwatch.GetElapsedTime(start, data).TotalMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(data, screenReady).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(screenReady, layout).TotalMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(layout).TotalMilliseconds);
    }
    public new void Hide()
    {
        if (_menu != null) _menu.IsOpen = false;
        if (!_shown) return;
        _currentPage?.StopMotion(); SpatialView?.StopMotion(); Ui.StopEntrance(_shell); _shown = false; OpenStateChanged?.Invoke(); Native.ClipPopup(this, true);
    }
    private System.Windows.Forms.Screen FitToScreen(Native.Point point)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)); var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (System.Windows.Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName) Native.Position(this, point.X, point.Y);
        double dpi = Math.Max(1, Native.GetDpiForWindow(handle) / 96d);
        Width = Math.Min(PreferredSize.Width, screen.WorkingArea.Width / dpi - 8); Height = Math.Min(PreferredSize.Height, screen.WorkingArea.Height / dpi - 8); return screen;
    }
    private void Place(Native.Point point, System.Windows.Forms.Screen screen)
    {
        double dpi = Math.Max(1, Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle) / 96d);
        var area = screen.WorkingArea;
        _bounds = SwitcherPlacement.Place(Palette.Layout, point.X, point.Y, Width * dpi, Height * dpi, new Rect(area.X, area.Y, area.Width, area.Height), dpi);
        _leftSide = _bounds.Right <= point.X; Native.Position(this, (int)_bounds.X, (int)_bounds.Y);
    }
}

internal static class RailPlacement
{
    internal static Rect Place(double x, double y, double width, double height, Rect area, double gap = 18)
    {
        width = Math.Min(width, area.Width); height = Math.Min(height, area.Height);
        double left = x + gap + width <= area.Right ? x + gap : x - gap - width;
        return new(Math.Clamp(left, area.Left, area.Right - width), Math.Clamp(y - height / 2, area.Top, area.Bottom - height), width, height);
    }
}

internal sealed class RailScrollViewer : ScrollViewer
{
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.Register("Offset", typeof(double), typeof(RailScrollViewer),
        new PropertyMetadata(0d, (sender, e) => ((RailScrollViewer)sender).ScrollToVerticalOffset((double)e.NewValue)));
    private double _target;
    internal void MoveBy(double amount)
    {
        _target = Math.Clamp(_target + amount, 0, ScrollableHeight); double from = VerticalOffset;
        if (!SystemParameters.ClientAreaAnimation) { Jump(_target); return; }
        SetCurrentValue(OffsetProperty, _target);
        BeginAnimation(OffsetProperty, new DoubleAnimation(from, _target, TimeSpan.FromMilliseconds(145)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
    }
    internal void Jump(double offset) { BeginAnimation(OffsetProperty, null); _target = Math.Clamp(offset, 0, ScrollableHeight); SetCurrentValue(OffsetProperty, _target); ScrollToVerticalOffset(_target); }
    internal void StopMotion() => Jump(VerticalOffset);
}
