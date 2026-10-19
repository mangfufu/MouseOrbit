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
    internal Task InitializeLauncher() => RefreshIndex();
    private readonly System.Windows.Threading.DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _observedId;
    private DateTime _observedSince;
    private bool _visitRecorded;
    private void ObserveUsage()
    {
        if (_indexing || !_controller.Settings.LearnApplications || _controller.Paused) return;
        var entry = OpenWindows.ForegroundApplication();
        if (entry?.Id != _observedId) { _observedId = entry?.Id; _observedSince = DateTime.UtcNow; _visitRecorded = false; return; }
        if (entry == null || _visitRecorded || DateTime.UtcNow - _observedSince < TimeSpan.FromSeconds(8)) return;
        try { _launcher.Observe(entry); _visitRecorded = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _visitRecorded = true; AssistantStatus.Text = "使用习惯记录无法保存：" + ex.Message; }
    }
    private async Task RefreshIndex()
    {
        if (_indexing) return;
        _indexing = true;
        try { await Task.Run(_launcher.Refresh); AssistantStatus.Text = ""; }
        catch (Exception ex) { AssistantStatus.Text = "索引失败：" + ex.Message; }
        finally { _indexing = false; RefreshResults(); }
    }
    private void RefreshResults()
    {
        if (!_ready || _indexing) return;
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
        _results = entries.Select(_launcher.WithUsage).ToList(); ResultsList.Tag = _filter; ResultsList.ItemsSource = _results; ResultsList.SelectedIndex = _results.Count > 0 ? 0 : -1;
        EmptyText.Text = _results.Count > 0 ? "" : "暂无项目";
        foreach (var tab in FilterTabs.Children.OfType<Button>()) tab.Background = (string)tab.Tag == _filter ? Palette.Selected : System.Windows.Media.Brushes.Transparent;
    }
    private void ShowAssistant()
    {
        AssistantPanel.Visibility = Visibility.Visible; SettingsPanel.Visibility = Visibility.Collapsed;
        LibraryNav.Background = Palette.Hover; SettingsNav.Background = System.Windows.Media.Brushes.Transparent;
        _filter = "frequent"; RefreshResults(); _ = RefreshIndex();
    }
    private void AssistantNav_Click(object sender, RoutedEventArgs e) => ShowAssistant();
    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    { AssistantPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible; SettingsNav.Background = Palette.Hover; LibraryNav.Background = System.Windows.Media.Brushes.Transparent; ContentScroll.ScrollToTop(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshIndex();
    private void QuickPreview_Click(object sender, RoutedEventArgs e) => _controller.OpenAssistant();
    private Dictionary<string, IReadOnlyList<DesktopEntry>> _quickSnapshot = new();
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
        try { _launcher.Record(entry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AssistantStatus.Text = "操作已完成，但使用记录无法保存：" + ex.Message; }
    }
    private async void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var parent = e.OriginalSource as DependencyObject; parent != null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            if (parent is Button) return;
        if (ResultsList.SelectedItem is DesktopEntry entry) await LaunchEntry(entry);
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
        try { _launcher.TogglePin(entry); AssistantStatus.Text = _launcher.IsPinned(entry) ? "已固定 · " + entry.Name : "已取消固定 · " + entry.Name; RefreshResults(); }
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
            if (_quickSnapshot.TryGetValue("frequent", out var items)) _quickSnapshot["frequent"] = items.Where(e => e.Id != entry.Id).ToArray();
            RefreshResults(); AssistantStatus.Text = ""; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AssistantStatus.Text = "移除失败：" + ex.Message; return false; }
    }
    private bool RemoveRecentEntry(DesktopEntry entry)
    {
        try
        {
            _launcher.RemoveRecent(entry);
            if (_quickSnapshot.TryGetValue("recent", out var recent)) _quickSnapshot["recent"] = recent.Where(e => e.Id != entry.Id).ToArray();
            RefreshResults(); AssistantStatus.Text = ""; return true;
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
            try { _launcher.Record(entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AssistantStatus.Text = "已打开，但使用记录无法保存：" + ex.Message; }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { Show(); AssistantStatus.Text = "打开失败：" + ex.Message; return false; }
        finally { _launching = false; }
    }
}
