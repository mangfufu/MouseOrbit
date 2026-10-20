using System.Windows.Controls;
using System.Windows.Media;

namespace MouseOrbit;

internal sealed class QuickPanelWindow : Window
{
    private readonly Dictionary<string, Button> _tabs = new();
    private Dictionary<string, IReadOnlyList<DesktopEntry>> _snapshot = new();
    private IReadOnlyList<SwitchGroup> _windows = [];
    private SwitchGroup? _expanded;
    private string _group = "windows";
    private Grid _rows = new();
    private ScrollViewer? _currentPage;
    private ScrollViewer _scroll = new();
    private TextBlock _notice = new();
    private TextBlock _heading = new();
    private TextBlock _section = new(), _count = new(), _countLabel = new();
    private PixelCrt _crt = new();
    private Button _back = null!;
    private Rect _bounds;
    private readonly Dictionary<string, ScrollViewer> _pages = new();
    private readonly Dictionary<ScrollViewer, List<LiveThumbnail>> _thumbnails = new();
    private sealed record WindowTileData(SwitchWindow Window, SwitchGroup? App, LiveThumbnail? Thumbnail);
    private readonly HashSet<ScrollViewer> _preparedPages = new();
    private Size _preparedSize;
    private int _builtLayoutClass;
    private int LayoutClass => Width < 550 ? 0 : Width < 670 ? 1 : Width < 800 ? 2 : 3;
    private string PageKey => _group == "windows" && _expanded != null ? "app:" + _expanded.Key : _group;
    private int _anchorScreen = 1;
    private bool _shown;
    public new bool IsVisible => _shown;
    internal event Action? OpenStateChanged;
    private int Columns => Width < 670 ? 2 : Width < 800 ? 3 : 4;
    public event Action<DesktopEntry>? Selected;
    public event Action? Manage;
    public event Action? Dismissed;
    public event Action<string>? ToolRequested;
    internal (double Data, double Screen, double Layout, double Show) LastOpenTimings { get; private set; }
    public Func<DesktopEntry, bool>? RemoveRecentRequested;
    public Func<DesktopEntry, bool>? RemoveFrequentRequested;
    public QuickPanelWindow()
    {
        Title = "MouseOrbit"; Width = 840; Height = 544;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        UseLayoutRounding = true; SnapsToDevicePixels = true; FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MouseOrbit;component/Controls.xaml", UriKind.Relative) });
        Palette.Resources(Resources); BuildLayout();
        SourceInitialized += (_, _) =>
        {
            Native.OverlayStyle(this, false); Native.DisableTransitions(this);
        };
        Palette.Changed += ThemeChanged;
        Closed += (_, _) => { _shown = false; OpenStateChanged?.Invoke(); Palette.Changed -= ThemeChanged; InvalidatePages(); };
    }
    private void ThemeChanged() { double offset = _scroll.VerticalOffset; BuildLayout(); _scroll.ScrollToVerticalOffset(offset); }
    private void BuildLayout()
    {
        InvalidatePages(); _builtLayoutClass = LayoutClass;
        _tabs.Clear();
        var shell = new Border { Background = Palette.Surface, BorderBrush = Palette.Edge, BorderThickness = new Thickness(1), CornerRadius = Palette.PanelCorner, Margin = new Thickness(5), Padding = new Thickness(14, 8, 14, 8) };
        Content = shell;
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new GridLength(50) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new GridLength(46) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); shell.Child = layout;
        bool compact = Width < 550;
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new GridLength(compact ? 78 : 144) }); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); layout.Children.Add(header);
        header.Children.Add(new TextBlock { Text = compact ? "ORBIT" : "MOUSE ORBIT", FontFamily = Palette.BrandFont, FontSize = Palette.Pixel ? 22 : 16, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tabs, 1); header.Children.Add(tabs);
        foreach (var (id, name) in new[] { ("windows", "窗口"), ("frequent", "常用"), ("recent", "最近") })
        {
            var tab = Palette.Button(name, () => { _expanded = null; SelectGroup(id); }, name); tab.FontSize = 12; tab.Margin = new Thickness(2, 0, 2, 0); tab.Padding = new Thickness(compact ? 7 : 12, 7, compact ? 7 : 12, 7);
            tabs.Children.Add(tab); _tabs[id] = tab;
        }
        var utility = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(utility, 2); header.Children.Add(utility);
        utility.Children.Add(Palette.Button("主题", () => { _expanded = null; SelectGroup("themes"); }, "选择主题"));
        utility.Children.Add(Palette.Button("专注", () => { Hide(); ToolRequested?.Invoke("focus"); }, "专注模式"));
        utility.Children.Add(Palette.Button("×", () => { Hide(); Dismissed?.Invoke(); }, "关闭"));
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = Width < 670 ? new GridLength(0) : new GridLength(164) }); body.ColumnDefinitions.Add(new()); Grid.SetRow(body, 1); layout.Children.Add(body);
        var art = new Grid { ClipToBounds = true, Margin = new Thickness(0, 12, 14, 12) }; body.Children.Add(art);
        var artStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _section = new TextBlock { FontFamily = Palette.BrandFont, FontSize = Palette.Pixel ? 24 : 13, Foreground = Palette.Muted, Margin = new Thickness(6, 0, 0, 14) }; artStack.Children.Add(_section);
        _crt = new PixelCrt { Width = 150, Height = 132 }; artStack.Children.Add(_crt);
        _count = new TextBlock { FontFamily = Palette.BrandFont, FontSize = Palette.Pixel ? 48 : 36, Foreground = Palette.Ink, Margin = new Thickness(6, 18, 0, 0) }; artStack.Children.Add(_count);
        _countLabel = new TextBlock { Foreground = Palette.Muted, FontSize = 11, Margin = new Thickness(8, 2, 0, 0) }; artStack.Children.Add(_countLabel);
        art.Children.Add(artStack);
        var main = new Grid { Margin = new Thickness(5, 8, 0, 8) }; main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new()); Grid.SetColumn(main, 1); body.Children.Add(main);
        var backRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; main.Children.Add(backRow);
        _back = Palette.Button("←", () => { _expanded = null; SelectGroup("windows"); }, "返回应用"); _back.Padding = new Thickness(9, 4, 9, 4); DockPanel.SetDock(_back, Dock.Left); backRow.Children.Add(_back);
        _heading = new TextBlock { Foreground = Palette.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) }; backRow.Children.Add(_heading);
        // Each page owns its scroll viewport. Hidden pages retain their layout without
        // increasing the active page's scroll extent or participating in hit testing.
        _rows = new Grid { ClipToBounds = true };
        Grid.SetRow(_rows, 1); main.Children.Add(_rows);
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 3, 0, 0) }; Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var management = Palette.Button("管理", () => { Hide(); Manage?.Invoke(); }, "管理"); management.Foreground = Palette.Muted; DockPanel.SetDock(management, Dock.Right); footer.Children.Add(management);
        foreach (var (id, name) in new[] { ("wheel", "轮盘"), ("capture", "截图") })
        {
            var button = Palette.Button(name, () => { Hide(); ToolRequested?.Invoke(id); }, name);
            button.Background = Palette.Hover; button.Padding = new Thickness(compact ? 8 : 18, 7, compact ? 8 : 18, 7); button.Margin = new Thickness(0, 0, compact ? 4 : 8, 0); footer.Children.Add(button);
        }
        _notice = new TextBlock { Foreground = Palette.Accent, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2), Visibility = Visibility.Collapsed }; Grid.SetRow(_notice, 3); layout.Children.Add(_notice);
        SelectGroup(_group);
    }
    public void Message(string text) { _notice.Text = text; _notice.Visibility = Visibility.Visible; }
    private void SelectGroup(string group)
    {
        _group = group; if (_scroll.VerticalOffset > 0) _scroll.ScrollToTop();
        _section.Text = group switch { "windows" => "WINDOWS", "recent" => "RECENTS", "themes" => "THEMES", _ => "FAVORITES" };
        _count.Text = (group == "windows" ? _windows.Sum(g => g.Windows.Count) : group == "themes" ? ThemePresets.All.Length : _snapshot.GetValueOrDefault(group)?.Count ?? 0).ToString("00");
        _countLabel.Text = group == "windows" ? "个窗口" : group == "themes" ? "种主题" : "个项目";
        foreach (var tab in _tabs) { tab.Value.Background = tab.Key == group ? Palette.Selected : Brushes.Transparent; }
        _back.Visibility = _expanded != null || group == "themes" ? Visibility.Visible : Visibility.Collapsed;
        _heading.Text = _expanded?.Name ?? (group == "themes" ? "主题" : "");
        if (_pages.TryGetValue(PageKey, out var page))
        {
            if (_currentPage != page)
            { if (_currentPage != null) _currentPage.Visibility = Visibility.Hidden; page.Visibility = Visibility.Visible; _currentPage = page; }
            _scroll = page;
            return;
        }
        if (_currentPage != null) _currentPage.Visibility = Visibility.Hidden;
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Columns, VerticalAlignment = VerticalAlignment.Top };
        var viewport = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        _pages[PageKey] = viewport; _currentPage = viewport; _scroll = viewport; _rows.Children.Add(viewport);
        if (group == "themes")
        {
            foreach (var preset in ThemePresets.All)
            {
                string selected = preset.Id;
                var sample = new StackPanel();
                var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
                foreach (string hex in new[] { preset.Paper, preset.Accent, preset.Ink })
                    chips.Children.Add(new Border { Background = Palette.B(hex), Width = 20, Height = 24, CornerRadius = new CornerRadius(preset.Pixel ? 0 : 4), Margin = new Thickness(2) });
                sample.Children.Add(chips); sample.Children.Add(new TextBlock { Text = preset.Name, FontSize = 11, Foreground = Palette.Ink, TextAlignment = TextAlignment.Center });
                var button = Palette.Button(sample, () => ToolRequested?.Invoke("theme:" + selected), preset.Name);
                button.Height = 112; button.Margin = new Thickness(3); button.Padding = new Thickness(4);
                button.Background = preset.Id == Palette.Current.Id ? Palette.Selected : Brushes.Transparent;
                grid.Children.Add(button);
            }
            return;
        }
        if (group == "windows")
        {
            if (_expanded != null) foreach (var window in _expanded.Windows) grid.Children.Add(WindowTile(window, null, viewport));
            else foreach (var app in _windows) grid.Children.Add(WindowTile(app.Recent, app, viewport));
            if (grid.Children.Count == 0) Empty("暂无其他窗口");
            return;
        }
        var entries = _snapshot.GetValueOrDefault(group) ?? [];
        foreach (var entry in entries) grid.Children.Add(EntryTile(entry, group));
        if (entries.Count == 0) Empty("暂无项目");
    }
    private void Empty(string message)
    {
        var empty = new TextBlock { Text = message, Foreground = Palette.Muted, FontSize = 13, Margin = new Thickness(12, 64, 0, 0) };
        if (_currentPage != null) _currentPage.Content = empty;
    }
    private void InvalidatePages()
    {
        foreach (var thumbnails in _thumbnails.Values) foreach (var thumbnail in thumbnails) thumbnail.Dispose();
        _thumbnails.Clear(); _preparedPages.Clear(); _pages.Clear(); _rows.Children.Clear(); _currentPage = null;
    }
    private void InvalidatePage(string key)
    {
        if (_pages.Remove(key, out var page))
        {
            if (_thumbnails.Remove(page, out var thumbnails)) foreach (var thumbnail in thumbnails) thumbnail.Dispose();
            _preparedPages.Remove(page); _rows.Children.Remove(page); if (_currentPage == page) _currentPage = null;
        }
    }
    private void InvalidateWindows()
    {
        foreach (var key in _pages.Keys.Where(key => key == "windows" || key.StartsWith("app:", StringComparison.Ordinal)).ToArray()) InvalidatePage(key);
    }
    private void SetSnapshot(Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot)
    {
        foreach (string group in new[] { "frequent", "recent" })
        {
            var previous = _snapshot.GetValueOrDefault(group) ?? []; var next = snapshot.GetValueOrDefault(group) ?? [];
            if (previous.Count != next.Count || !previous.Zip(next).All(pair => pair.First.Id == pair.Second.Id &&
                pair.First.Name == pair.Second.Name && pair.First.Kind == pair.Second.Kind && pair.First.Target == pair.Second.Target))
                UpdateEntryPage(group, next);
        }
        _snapshot = snapshot.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DesktopEntry>)pair.Value.ToArray());
    }
    private static bool SameEntry(DesktopEntry first, DesktopEntry second) => first.Id == second.Id && first.Name == second.Name && first.Kind == second.Kind && first.Target == second.Target;
    private static bool SameWindow(SwitchWindow first, SwitchWindow second) => first.Handle == second.Handle && first.Process == second.Process &&
        first.Title == second.Title && first.Minimized == second.Minimized && first.Screen == second.Screen && first.IconEntry == second.IconEntry && first.Preview == second.Preview;
    private static bool SameGroup(SwitchGroup first, SwitchGroup second) => first.Key == second.Key && first.Name == second.Name && first.Windows.Count == second.Windows.Count &&
        first.Windows.Zip(second.Windows).All(pair => SameWindow(pair.First, pair.Second));
    private static string TileKey(FrameworkElement tile) => tile.Tag switch
    {
        DesktopEntry entry => entry.Id,
        WindowTileData data => data.App == null ? "window:" + data.Window.Handle : data.App.Key,
        _ => ""
    };
    private void PatchPage<T>(ScrollViewer page, IReadOnlyList<T> items, Func<T, string> key,
        Func<FrameworkElement, T, bool> same, Func<T, FrameworkElement> create, string emptyMessage)
    {
        if (items.Count == 0 && page.Content is TextBlock) return;
        var grid = page.Content as System.Windows.Controls.Primitives.UniformGrid;
        if (grid == null) { grid = new() { Columns = Columns, VerticalAlignment = VerticalAlignment.Top }; page.Content = grid; }
        var old = grid.Children.OfType<FrameworkElement>().ToLookup(TileKey);
        var wanted = items.Select(item => old[key(item)].FirstOrDefault(tile => same(tile, item)) ?? create(item)).ToArray();
        if (items.Count > 0 && grid.Children.OfType<FrameworkElement>().SequenceEqual(wanted)) return;
        var retained = wanted.ToHashSet();
        foreach (var tile in grid.Children.OfType<FrameworkElement>().Where(tile => !retained.Contains(tile)).ToArray())
        {
            if (tile.Tag is WindowTileData { Thumbnail: { } thumbnail })
            { thumbnail.Dispose(); if (_thumbnails.TryGetValue(page, out var previews)) previews.Remove(thumbnail); }
            grid.Children.Remove(tile);
        }
        for (int i = 0; i < wanted.Length; i++)
        {
            if (i < grid.Children.Count && grid.Children[i] == wanted[i]) continue;
            grid.Children.Remove(wanted[i]); grid.Children.Insert(i, wanted[i]);
        }
        if (items.Count == 0) page.Content = new TextBlock { Text = emptyMessage, Foreground = Palette.Muted, FontSize = 13, Margin = new Thickness(12, 64, 0, 0) };
        _preparedPages.Remove(page);
    }
    private void UpdateEntryPage(string group, IReadOnlyList<DesktopEntry> entries)
    {
        if (_pages.TryGetValue(group, out var page)) PatchPage(page, entries, entry => entry.Id,
            (tile, entry) => tile.Tag is DesktopEntry previous && SameEntry(previous, entry), entry => EntryTile(entry, group), "暂无项目");
    }
    private FrameworkElement EntryTile(DesktopEntry entry, string group)
    {
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new EntryIcon { Entry = entry, Width = 32, Height = 32, Margin = new Thickness(0, 8, 0, 12) });
        content.Children.Add(new TextBlock { Text = entry.ShortName, Foreground = Palette.Ink, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var button = Palette.Button(content, () => { Hide(); Selected?.Invoke(entry); }, entry.Name);
        button.Height = 112; button.Padding = new Thickness(5); button.Margin = new Thickness(3);
        var tile = new Grid { Tag = entry }; tile.Children.Add(button);
        if (group is "recent" or "frequent")
        {
            var remove = Palette.Button("×", () =>
            {
                var handler = group == "recent" ? RemoveRecentRequested : RemoveFrequentRequested;
                if (handler?.Invoke(entry) != true) { Message("移除失败，请重试"); return; }
                var offset = _scroll.VerticalOffset;
                _snapshot[group] = (_snapshot.GetValueOrDefault(group) ?? []).Where(e => e.Id != entry.Id).ToArray();
                UpdateEntryPage(group, _snapshot[group]);
                SelectGroup(group); _scroll.ScrollToVerticalOffset(offset);
            }, (group == "recent" ? "从最近移除：" : "从常用移除：") + entry.Name);
            remove.Width = remove.Height = 22; remove.Padding = new Thickness(3); remove.FontSize = 12;
            remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top; remove.Opacity = .6;
            tile.MouseEnter += (_, _) => remove.Opacity = 1; tile.MouseLeave += (_, _) => remove.Opacity = .6;
            tile.Children.Add(remove);
        }
        return tile;
    }
    private FrameworkElement WindowTile(SwitchWindow window, SwitchGroup? app, ScrollViewer page)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new GridLength(72) }); grid.RowDefinitions.Add(new() { Height = new GridLength(32) });
        var placeholder = new Grid { Background = Palette.Hover };
        var fallback = new EntryIcon { Entry = window.Icon, Width = 30, Height = 30, Opacity = .5 }; placeholder.Children.Add(fallback);
        var preview = new Image { Stretch = Stretch.UniformToFill, Source = window.Preview }; placeholder.Children.Add(preview);
        var state = new TextBlock { Text = window.Minimized ? "已最小化" : "", FontSize = 10, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5) }; placeholder.Children.Add(state);
        grid.Children.Add(new Border { BorderBrush = Palette.Edge, BorderThickness = new Thickness(1), Child = placeholder, ClipToBounds = true });
        var label = new Grid { Margin = new Thickness(0, 6, 0, 0) }; label.ColumnDefinitions.Add(new() { Width = new GridLength(22) }); label.ColumnDefinitions.Add(new()); label.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetRow(label, 1); grid.Children.Add(label);
        label.Children.Add(new EntryIcon { Entry = window.Icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top });
        var name = new TextBlock { Text = app == null ? window.Title : new DesktopEntry("", app.Name, "", "", "", "").ShortName, Foreground = Palette.Ink, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(3, 0, 4, 0) }; Grid.SetColumn(name, 1); label.Children.Add(name);
        if (app?.Windows.Count > 1)
        {
            var count = new TextBlock { Text = app.Windows.Count.ToString("00"), FontFamily = Palette.PixelFont, FontSize = 18, Foreground = Palette.Accent }; Grid.SetColumn(count, 2); label.Children.Add(count);
        }
        else if (window.Screen != _anchorScreen)
        {
            var monitor = new TextBlock { Text = "屏" + window.Screen, FontSize = 10, Foreground = Palette.Muted }; Grid.SetColumn(monitor, 2); label.Children.Add(monitor);
        }
        var button = Palette.Button(grid, () =>
        {
            if (app?.Windows.Count > 1) { _expanded = app; SelectGroup("windows"); return; }
            WindowNative.GetWindowThreadProcessId(window.Handle, out uint pid);
            if (!WindowNative.IsWindow(window.Handle) || pid != window.Process) { Message("这个窗口已经关闭"); return; }
            Hide(); Selected?.Invoke(window.Entry);
        }, app?.Name ?? window.Title);
        button.Height = 122; button.Padding = new Thickness(6); button.Margin = new Thickness(2, 2, 2, 6);
        LiveThumbnail? thumbnail = null;
        if (window.Handle != 0 && window.Preview == null)
        {
            if (!_thumbnails.TryGetValue(page, out var thumbnails)) _thumbnails[page] = thumbnails = new();
            thumbnail = new LiveThumbnail(this, placeholder, page, window); thumbnails.Add(thumbnail);
        }
        button.Tag = new WindowTileData(window, app, thumbnail);
        return button;
    }
    public bool Contains(Native.Point point) => IsVisible && _bounds.Contains(point.X, point.Y);
    internal void SetEntries(Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot) { SetSnapshot(snapshot); _expanded = null; SelectGroup("windows"); }
    internal void Prepare(Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot)
    {
        Native.GetCursorPos(out var point); FitToScreen(point);
        SetSnapshot(snapshot);
        PreparePages(_preparedPages.Count == 0);
        if (!base.IsVisible) { Native.ClipPopup(this, true); base.Show(); }
    }
    private void PreparePages(bool rasterize = true)
    {
        var size = new Size(Width, Height);
        if (_preparedSize != size) { _preparedSize = size; _preparedPages.Clear(); }
        string selected = _group; var expanded = _expanded;
        var content = (FrameworkElement)Content;
        void PreparePage(string group, SwitchGroup? app = null)
        {
            string key = app == null ? group : "app:" + app.Key;
            if (_pages.TryGetValue(key, out var cached) && _preparedPages.Contains(cached)) return;
            _expanded = app; SelectGroup(group);
            if (_currentPage == null || !_preparedPages.Add(_currentPage)) return;
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            // Complete template creation, text shaping and first drawing while hidden.
            // Visibility.Hidden retains this work when the user selects another page.
            if (rasterize)
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(_currentPage.ActualWidth)), Math.Max(1, (int)Math.Ceiling(_currentPage.ActualHeight)),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(_currentPage);
            }
        }
        foreach (string group in new[] { "frequent", "recent", "themes", "windows" }) PreparePage(group);
        foreach (var app in _windows.Where(app => app.Windows.Count > 1)) PreparePage("windows", app);
        _expanded = expanded; SelectGroup(selected);
        content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
    }
    internal void SetWindows(IReadOnlyList<SwitchGroup> groups)
    {
        groups = groups.Select(group => group with { Windows = group.Windows.ToArray() }).ToArray();
        bool same = _windows.Count == groups.Count && _windows.Zip(groups).All(pair => SameGroup(pair.First, pair.Second));
        _windows = groups; _expanded = null;
        if (!same)
        {
            if (_pages.TryGetValue("windows", out var page)) PatchPage(page, groups, group => group.Key,
                (tile, group) => tile.Tag is WindowTileData { App: { } previous } && SameGroup(previous, group),
                group => WindowTile(group.Recent, group, page), "暂无其他窗口");
            foreach (var key in _pages.Keys.Where(key => key.StartsWith("app:", StringComparison.Ordinal)).ToArray())
            {
                var group = groups.FirstOrDefault(group => "app:" + group.Key == key);
                if (group == null) { InvalidatePage(key); continue; }
                var appPage = _pages[key]; PatchPage(appPage, group.Windows, window => "window:" + window.Handle,
                    (tile, window) => tile.Tag is WindowTileData previous && SameWindow(previous.Window, window), window => WindowTile(window, null, appPage), "暂无其他窗口");
            }
        }
        if (_builtLayoutClass != LayoutClass) BuildLayout(); else if (IsVisible) SelectGroup(_group);
    }
    public void Open(Native.Point point, Dictionary<string, IReadOnlyList<DesktopEntry>> snapshot)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        SetSnapshot(snapshot); _expanded = null; _group = "windows";
        long data = System.Diagnostics.Stopwatch.GetTimestamp();
        var screen = FitToScreen(point);
        var dpi = Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle) / 96d;
        SelectGroup(_group);
        long screenReady = System.Diagnostics.Stopwatch.GetTimestamp();
        // Changed hidden pages keep their measured controls. Re-rendering all of
        // them to offscreen bitmaps on every tap was itself an opening-time stall.
        if (_preparedPages.Count == 0) PreparePages();
        else UpdateLayout();
        long layout = System.Diagnostics.Stopwatch.GetTimestamp();
        var area = screen.WorkingArea;
        _bounds = PanelPlacement.Place(point.X, point.Y, Width * dpi, Height * dpi, new Rect(area.X, area.Y, area.Width, area.Height));
        Native.Position(this, (int)_bounds.X, (int)_bounds.Y);
        if (!base.IsVisible) { Native.ClipPopup(this, true); base.Show(); }
        Native.ClipPopup(this, false); _shown = true;
        Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, new nint(-1), 0, 0, 0, 0, 0x0213);
        OpenStateChanged?.Invoke();
        LastOpenTimings = (System.Diagnostics.Stopwatch.GetElapsedTime(start, data).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(data, screenReady).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(screenReady, layout).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(layout).TotalMilliseconds);
    }
    public new void Hide()
    {
        if (!_shown) return;
        _shown = false; OpenStateChanged?.Invoke();
        // Keep WPF's prepared presentation surface. An empty native region hides
        // every pixel and removes the window from mouse hit testing immediately.
        Native.ClipPopup(this, true);
    }
    private System.Windows.Forms.Screen FitToScreen(Native.Point point)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        // A same-monitor reopen already has the correct DPI; moving out and back
        // merely to query it causes extra native layout/compositor work.
        if (System.Windows.Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName) Native.Position(this, point.X, point.Y);
        double dpi = Math.Max(1, Native.GetDpiForWindow(handle) / 96d);
        int anchor = Array.FindIndex(System.Windows.Forms.Screen.AllScreens, s => s.DeviceName == screen.DeviceName) + 1;
        if (anchor != _anchorScreen) InvalidateWindows(); _anchorScreen = anchor;
        Width = Math.Min(840, screen.WorkingArea.Width / dpi - 10); Height = Math.Min(544, screen.WorkingArea.Height / dpi - 10);
        if (_builtLayoutClass != LayoutClass) BuildLayout();
        return screen;
    }
}
