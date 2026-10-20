using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

namespace MouseOrbit;

public partial class MainWindow
{
    private readonly Launcher _launcher = new();
    private bool _indexing;
    private string _filter = "frequent";
    private List<DesktopEntry> _results = new();
    private bool _launching;
    private long _lastIndexRefresh;
    private bool _resultsDirty;
    private readonly Dictionary<string, ListBox> _resultViews = new();
    private ListBox? _activeResultsView;
    internal async Task InitializeLauncher()
    {
        await RefreshIndex();
        foreach (var command in Commands.All) CommandIcons.Get(command.Id);
    }
    private readonly System.Windows.Threading.DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _observedId;
    private DateTime _observedSince;
    private bool _visitRecorded;
    private bool _observingUsage;
    private async void ObserveUsage()
    {
        if (_observingUsage || _indexing || !_controller.Settings.LearnApplications || _controller.Paused || _controller.Interacting) return;
        _observingUsage = true;
        var foreground = Native.GetForegroundWindow();
        try
        {
            // Process modules and executable version resources can touch slow disks.
            var entry = await Task.Run(OpenWindows.ForegroundApplication);
            if (_indexing || _controller.Interacting || foreground != Native.GetForegroundWindow()) return;
            if (entry?.Id != _observedId) { _observedId = entry?.Id; _observedSince = DateTime.UtcNow; _visitRecorded = false; return; }
            if (entry == null || _visitRecorded || DateTime.UtcNow - _observedSince < TimeSpan.FromSeconds(8)) return;
            try { _launcher.Observe(entry); _visitRecorded = true; _quickNeedsPreparation = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _visitRecorded = true; AssistantStatus.Text = "使用习惯记录无法保存：" + ex.Message; }
        }
        finally { _observingUsage = false; }
    }
    private void RefreshIndexIfStale()
    { if (!IsVisible && !_controller.Interacting && Environment.TickCount64 - _lastIndexRefresh >= 30_000) _ = RefreshIndex(); }
    private async Task RefreshIndex()
    {
        if (_indexing) return;
        _indexing = true;
        try
        {
            var scan = _launcher.CreateScan();
            var entries = await Task.Run(scan);
            await ShellIcons.PreloadAsync(entries.Select(_launcher.WithUsage).OrderByDescending(entry => entry.Pinned)
                .ThenByDescending(entry => entry.LastUsed).Concat(entries));
            _launcher.ApplyScan(entries); AssistantStatus.Text = "";
        }
        catch (Exception ex) { AssistantStatus.Text = "索引失败：" + ex.Message; }
        finally
        {
            _indexing = false; _lastIndexRefresh = Environment.TickCount64;
            if (IsVisible) RefreshResults(); else _resultsDirty = true;
            _quickNeedsPreparation = true;
        }
    }
    private void RefreshResults()
    {
        if (!_ready) return;
        _resultsDirty = false;
        IEnumerable<DesktopEntry> entries;
        {
            ResultsHeading.Text = _filter switch { "recent" => "最近使用", "frequent" => "常用与固定", "apps" => "应用", "windows" => "窗口管理", "system" => "桌面与媒体", _ => "常用入口与最近访问" };
            entries = _filter switch
            {
                "recent" => _launcher.Recent(), "frequent" => _launcher.Frequent(),
                "apps" => _launcher.Entries.Where(e => e.Kind == "window").Concat(_launcher.Entries.Where(e => e.Kind == "app").OrderBy(e => e.Name)),
                "windows" => _launcher.Entries.Where(e => e.Kind == "command" && Commands.Find(e.Target).Kind == CommandKind.Window),
                "system" => _launcher.Entries.Where(e => e.Kind == "command" && Commands.Find(e.Target).Kind != CommandKind.Window && !new[] { "copy", "paste", "cut", "undo", "selectAll", "save", "search", "back", "forward", "top", "bottom", "newTab", "closeTab", "reopen", "refresh" }.Contains(e.Target)),
                _ => _launcher.Frequent().Take(4).Concat(_launcher.Entries.Where(e => e.Kind == "window").Take(3)).Concat(_launcher.Recent().Take(5)).Concat(_launcher.Entries.Where(e => e.Kind == "command" && new[] { "downloads", "explorer", "taskView", "settings" }.Contains(e.Target))).DistinctBy(e => e.Id)
            };
        }
        _results = entries.Select(_launcher.WithUsage).ToList();
        var view = ResultView(_filter);
        if (view.ItemsSource is not System.Collections.ObjectModel.ObservableCollection<DesktopEntry> visible)
        { visible = new(_results); view.ItemsSource = visible; }
        else
        {
            var ids = _results.Select(entry => entry.Id).ToHashSet();
            for (int i = visible.Count - 1; i >= 0; i--) if (!ids.Contains(visible[i].Id)) visible.RemoveAt(i);
            for (int i = 0; i < _results.Count; i++)
            {
                var entry = _results[i];
                if (i < visible.Count && visible[i].Id == entry.Id) { if (visible[i] != entry) visible[i] = entry; continue; }
                int found = -1;
                for (int j = i + 1; j < visible.Count; j++) if (visible[j].Id == entry.Id) { found = j; break; }
                if (found < 0) visible.Insert(i, entry); else { visible.Move(found, i); if (visible[i] != entry) visible[i] = entry; }
            }
            while (visible.Count > _results.Count) visible.RemoveAt(visible.Count - 1);
        }
        if (view.SelectedIndex < 0 && visible.Count > 0) view.SelectedIndex = 0;
        if (_activeResultsView != view)
        {
            if (_activeResultsView != null) _activeResultsView.Visibility = Visibility.Hidden;
            view.Visibility = Visibility.Visible; _activeResultsView = view;
        }
        EmptyText.Text = _results.Count > 0 ? "" : "暂无项目";
        foreach (var tab in FilterTabs.Children.OfType<Button>()) tab.Background = (string)tab.Tag == _filter ? Palette.Selected : System.Windows.Media.Brushes.Transparent;
    }
    private void RefreshVisibleResults()
    { if (IsVisible) RefreshResults(); else _resultsDirty = true; }
    private void ShowAssistant()
    {
        AssistantPanel.Visibility = Visibility.Visible; SettingsPanel.Visibility = Visibility.Hidden;
        LibraryNav.Background = Palette.Hover; SettingsNav.Background = System.Windows.Media.Brushes.Transparent;
        _filter = "frequent"; RefreshResults();
    }
    private void AssistantNav_Click(object sender, RoutedEventArgs e) => ShowAssistant();
    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    { AssistantPanel.Visibility = Visibility.Hidden; SettingsPanel.Visibility = Visibility.Visible; SettingsNav.Background = Palette.Hover; LibraryNav.Background = System.Windows.Media.Brushes.Transparent; ContentScroll.ScrollToTop(); }
    private ListBox ResultView(string filter)
    {
        if (_resultViews.Count == 0) { _resultViews["frequent"] = ResultsList; _activeResultsView = ResultsList; }
        if (!_resultViews.TryGetValue(filter, out var view))
        {
            view = new ListBox { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                ItemsPanel = ResultsList.ItemsPanel, ItemTemplate = ResultsList.ItemTemplate, ItemContainerStyle = ResultsList.ItemContainerStyle,
                Visibility = Visibility.Hidden };
            ScrollViewer.SetHorizontalScrollBarVisibility(view, ScrollBarVisibility.Disabled); ScrollViewer.SetVerticalScrollBarVisibility(view, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(view, true); VirtualizingPanel.SetIsVirtualizing(view, true); VirtualizingPanel.SetVirtualizationMode(view, VirtualizationMode.Recycling);
            _resultViews[filter] = view; ResultsHost.Children.Insert(0, view);
        }
        view.Tag = filter; return view;
    }
    internal void PrepareView()
    {
        var root = (FrameworkElement)Content; var size = new Size(Width, Height); string selected = _filter;
        void Draw()
        {
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width, (int)Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(root);
        }
        foreach (string filter in new[] { "frequent", "recent", "apps", "windows", "system" }) { _filter = filter; RefreshResults(); Draw(); }
        AssistantPanel.Visibility = Visibility.Hidden; SettingsPanel.Visibility = Visibility.Visible; Draw();
        SettingsPanel.Visibility = Visibility.Hidden; AssistantPanel.Visibility = Visibility.Visible;
        _filter = selected; RefreshResults(); Draw();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshIndex();
    private void QuickPreview_Click(object sender, RoutedEventArgs e) => _controller.OpenAssistant();
    private Dictionary<string, IReadOnlyList<DesktopEntry>> _quickSnapshot = new();
    private bool _quickNeedsPreparation;
    private void PrepareChangedQuickPage()
    {
        if (!_quickNeedsPreparation || _indexing || _controller.Interacting) return;
        _quickNeedsPreparation = false; _controller.PrepareAssistant();
    }
    private Dictionary<string, IReadOnlyList<DesktopEntry>> BuildQuickEntries()
    {
        if (_indexing) return _quickSnapshot;
        var defaults = new[] { "explorer", "downloads", "taskView", "desktop", "screenshot", "settings" }.Select(id => Commands.Find(id))
            .Select(c => new DesktopEntry("command:" + c.Id, c.Name, "系统操作", "command", c.Id, c.Symbol));
        _quickSnapshot = new()
        {
            ["frequent"] = _launcher.Frequent().Concat(defaults).Where(e => !_launcher.IsHiddenFromFrequent(e)).DistinctBy(e => e.Id).Select(_launcher.WithUsage).ToArray(),
            ["recent"] = _launcher.Recent().Select(_launcher.WithUsage).ToArray(),
            ["windows"] = []
        };
        return _quickSnapshot;
    }
    private void RecordQuickUsage(DesktopEntry entry)
    {
        try { _launcher.Record(entry); _quickNeedsPreparation = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AssistantStatus.Text = "操作已完成，但使用记录无法保存：" + ex.Message; }
    }
    private async void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var parent = e.OriginalSource as DependencyObject; parent != null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            if (parent is Button) return;
        if (_activeResultsView?.SelectedItem is DesktopEntry entry) await LaunchEntry(entry);
    }
    private void Entries_DragOver(object sender, DragEventArgs e)
    { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Link; e.Handled = true; } }
    private async void Entries_Drop(object sender, DragEventArgs e)
    {
        if (_indexing || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        try { _launcher.AddCustom((string[])e.Data.GetData(DataFormats.FileDrop)); _filter = "frequent"; await RefreshIndex(); AssistantStatus.Text = "已添加并固定入口"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AssistantStatus.Text = "添加失败：" + ex.Message; }
    }
    private void Filter_Click(object sender, RoutedEventArgs e) { _filter = (string)((Button)sender).Tag; RefreshResults(); }
    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchEntry((DesktopEntry)((Button)sender).Tag);
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var entry = (DesktopEntry)((Button)sender).Tag;
        try { _launcher.TogglePin(entry); _quickNeedsPreparation = true; AssistantStatus.Text = _launcher.IsPinned(entry) ? "已固定 · " + entry.Name : "已取消固定 · " + entry.Name; RefreshResults(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AssistantStatus.Text = "无法保存固定项：" + ex.Message; }
    }
    private void RemoveRecent_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var entry = (DesktopEntry)((Button)sender).Tag;
        if (_filter == "frequent") RemoveFrequentEntry(entry); else if (_filter == "recent") RemoveRecentEntry(entry);
    }
    private bool RemoveFrequentEntry(DesktopEntry entry)
    {
        try
        {
            _launcher.RemoveFrequent(entry);
            _quickNeedsPreparation = true;
            if (_quickSnapshot.TryGetValue("frequent", out var items)) _quickSnapshot["frequent"] = items.Where(e => e.Id != entry.Id).ToArray();
            RefreshVisibleResults(); AssistantStatus.Text = ""; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AssistantStatus.Text = "移除失败：" + ex.Message; return false; }
    }
    private bool RemoveRecentEntry(DesktopEntry entry)
    {
        try
        {
            _launcher.RemoveRecent(entry);
            _quickNeedsPreparation = true;
            if (_quickSnapshot.TryGetValue("recent", out var recent)) _quickSnapshot["recent"] = recent.Where(e => e.Id != entry.Id).ToArray();
            RefreshVisibleResults(); AssistantStatus.Text = ""; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AssistantStatus.Text = "移除失败：" + ex.Message; return false; }
    }
    private async Task<bool> LaunchEntry(DesktopEntry entry)
    {
        if (_launching) return false;
        _launching = true;
        try
        {
            if (entry.Kind == "command") { if (!await _controller.ExecuteFromAssistant(Commands.Find(entry.Target))) { Show(); return false; } }
            else { Launcher.Open(entry); Hide(); }
            try { _launcher.Record(entry); _quickNeedsPreparation = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AssistantStatus.Text = "已打开，但使用记录无法保存：" + ex.Message; }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { Show(); AssistantStatus.Text = "打开失败：" + ex.Message; return false; }
        finally { _launching = false; }
    }
}
