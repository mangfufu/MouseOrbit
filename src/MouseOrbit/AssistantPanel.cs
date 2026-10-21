using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

namespace MouseOrbit;

public partial class MainWindow
{
    private readonly LauncherSession _session;
    private readonly Launcher _launcher;
    private bool _indexing => _session.Indexing;
    private string _filter = "frequent";
    private List<DesktopEntry> _results = new();
    private bool _launching;
    private bool _resultsDirty;
    private readonly Dictionary<string, ListBox> _resultViews = new();
    private ListBox? _activeResultsView;
    internal Task InitializeLauncher() => _session.RefreshAsync();
    private Task RefreshIndex() => _session.RefreshAsync();
    private void SessionChanged()
    {
        if (IsVisible) RefreshResults(); else _resultsDirty = true;
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
        // Layout only the selected page; no full-window bitmap or hidden library tabs.
        RefreshResults();
        var root = (FrameworkElement)Content; var size = new Size(Width, Height);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshIndex();
    private void QuickPreview_Click(object sender, RoutedEventArgs e) => _controller.OpenAssistant();
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
        try { _launcher.TogglePin(entry); _session.NotifyChanged(); AssistantStatus.Text = _launcher.IsPinned(entry) ? "已固定 · " + entry.Name : "已取消固定 · " + entry.Name; RefreshResults(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AssistantStatus.Text = "无法保存固定项：" + ex.Message; }
    }
    private void RemoveRecent_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var entry = (DesktopEntry)((Button)sender).Tag;
        if (_filter == "frequent") RemoveFrequentEntry(entry); else if (_filter == "recent") RemoveRecentEntry(entry);
    }
    private bool RemoveFrequentEntry(DesktopEntry entry) => _session.Remove(entry, true);
    private bool RemoveRecentEntry(DesktopEntry entry) => _session.Remove(entry, false);
    private async Task<bool> LaunchEntry(DesktopEntry entry)
    {
        if (_launching) return false;
        _launching = true;
        try
        {
            if (entry.Kind == "command") { if (!await _controller.ExecuteFromAssistant(Commands.Find(entry.Target))) { Show(); return false; } }
            else { Launcher.Open(entry); Hide(); }
            _session.Record(entry);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { Show(); AssistantStatus.Text = "打开失败：" + ex.Message; return false; }
        finally { _launching = false; }
    }
}
