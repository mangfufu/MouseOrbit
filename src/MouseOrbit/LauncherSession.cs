using System.Threading.Tasks;
using System.Windows.Threading;

namespace MouseOrbit;

// Data and usage learning live independently of the optional management window.
internal sealed class LauncherSession : IDisposable
{
    internal Launcher Launcher { get; }
    internal bool Indexing { get; private set; }
    internal long LastRefresh { get; private set; }
    internal event Action? Changed;
    internal event Action<string>? Status;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Func<bool>? _busy;
    private Preferences? _settings;
    private string? _observedId;
    private DateTime _observedSince;
    private bool _visitRecorded, _observing, _dirty, _disposed;
    internal LauncherSession(Launcher launcher) => Launcher = launcher;
    internal void Start(Preferences settings, Func<bool> busy)
    {
        _settings = settings; _busy = busy;
        _timer.Tick += Tick; _timer.Start();
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (_disposed || _busy?.Invoke() == true) return;
        if (Environment.TickCount64 - LastRefresh >= 30_000) _ = RefreshAsync();
        _ = ObserveUsage();
        if (_dirty && !Indexing) { _dirty = false; Changed?.Invoke(); }
    }
    internal async Task RefreshAsync()
    {
        if (Indexing || _disposed) return;
        Indexing = true;
        try
        {
            var scan = Launcher.CreateScan(); var entries = await Task.Run(scan);
            if (_disposed) return;
            Launcher.ApplyScan(entries);
            // Only the side panel's immediately available entries need warm icons.
            // The full application library loads icons through EntryIcon on demand.
            await ShellIcons.PreloadAsync(BuildQuickEntries().Values.SelectMany(items => items));
            if (!_disposed) Status?.Invoke("");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!_disposed) Status?.Invoke("索引失败：" + ex.Message); }
        finally
        {
            Indexing = false; LastRefresh = Environment.TickCount64;
            if (!_disposed) { _dirty = true; Changed?.Invoke(); }
        }
    }
    private async Task ObserveUsage()
    {
        if (_observing || Indexing || _settings?.LearnApplications != true || _busy?.Invoke() == true) return;
        _observing = true; var foreground = Native.GetForegroundWindow();
        try
        {
            var entry = await Task.Run(OpenWindows.ForegroundApplication);
            if (_disposed || Indexing || _busy?.Invoke() == true || foreground != Native.GetForegroundWindow()) return;
            if (entry?.Id != _observedId) { _observedId = entry?.Id; _observedSince = DateTime.UtcNow; _visitRecorded = false; return; }
            if (entry == null || _visitRecorded || DateTime.UtcNow - _observedSince < TimeSpan.FromSeconds(8)) return;
            Launcher.Observe(entry); _visitRecorded = true; _dirty = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { _visitRecorded = true; if (!_disposed) Status?.Invoke("使用习惯记录无法保存：" + ex.Message); }
        finally { _observing = false; }
    }
    internal Dictionary<string, IReadOnlyList<DesktopEntry>> BuildQuickEntries()
    {
        var defaults = new[] { "explorer", "downloads", "taskView", "desktop", "screenshot", "settings" }.Select(Commands.Find)
            .Select(c => new DesktopEntry("command:" + c.Id, c.Name, "系统操作", "command", c.Id, c.Symbol));
        return new()
        {
            ["frequent"] = Launcher.Frequent().Concat(defaults).Where(e => !Launcher.IsHiddenFromFrequent(e)).DistinctBy(e => e.Id).Select(Launcher.WithUsage).ToArray(),
            ["recent"] = Launcher.Recent().Select(Launcher.WithUsage).ToArray(),
            ["windows"] = []
        };
    }
    internal bool Remove(DesktopEntry entry, bool frequent)
    {
        try
        {
            if (frequent) Launcher.RemoveFrequent(entry); else Launcher.RemoveRecent(entry);
            NotifyChanged(); Status?.Invoke(""); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status?.Invoke("移除失败：" + ex.Message); return false; }
    }
    internal void Record(DesktopEntry entry)
    {
        try { Launcher.Record(entry); NotifyChanged(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status?.Invoke("操作已完成，但使用记录无法保存：" + ex.Message); }
    }
    internal void NotifyChanged() { _dirty = true; Changed?.Invoke(); }
    public void Dispose() { _disposed = true; _timer.Stop(); _timer.Tick -= Tick; Changed = null; Status = null; }
}
