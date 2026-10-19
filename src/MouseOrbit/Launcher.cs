using System.Diagnostics;
using System.Text.Json;

namespace MouseOrbit;

public sealed record DesktopEntry(string Id, string Name, string Detail, string Kind, string Target, string Symbol)
{
    public int OpenCount { get; init; }
    public bool Pinned { get; init; }
    public DateTime? LastUsed { get; init; }
    public string PinSymbol => Pinned ? "★" : "☆";
    public string ShortName => Name switch
    {
        "Google Chrome" => "Chrome", "Microsoft Edge" => "Edge", "Visual Studio Code" => "VS Code",
        "Windows Terminal" => "终端", _ => Name
    };
    public string OpenLabel => Kind == "command" ? "执行 ↗" : Kind == "window" ? "切换 ↗" : "打开 ↗";
    public string Caption => Kind switch { "app" => "应用", "recent" => "最近文件", "folder" => "文件夹", "command" => "系统操作", "window" => "切换窗口", _ => "快捷入口" };
}
public sealed class UsageRecord
{
    public int Count { get; set; }
    public DateTime LastUsed { get; set; }
    public bool Pinned { get; set; }
    public DateTime? RemovedFromRecentAt { get; set; }
    public bool HiddenFromFrequent { get; set; }
}
public sealed class Launcher
{
    private readonly Dictionary<string, UsageRecord> _usage;
    private readonly List<DesktopEntry> _entries = new();
    private List<string> _custom = new();
    private Dictionary<string, DesktopEntry> _observed = new();
    private readonly string _dataDirectory;
    private string UsagePath => Path.Combine(_dataDirectory, "usage.json");
    private string CustomPath => Path.Combine(_dataDirectory, "shortcuts.json");
    private string ObservedPath => Path.Combine(_dataDirectory, "applications.json");
    public int AppCount => _entries.Count(e => e.Kind == "app");
    public IReadOnlyList<DesktopEntry> Entries => _entries;
    public Launcher() : this(Path.GetDirectoryName(Preferences.FilePath)!) { }
    internal Launcher(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        try { _usage = File.Exists(UsagePath) ? JsonSerializer.Deserialize<Dictionary<string, UsageRecord>>(File.ReadAllText(UsagePath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _usage = new(); }
        try { if (File.Exists(CustomPath)) _custom = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CustomPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _custom = new(); }
        try { if (File.Exists(ObservedPath)) _observed = JsonSerializer.Deserialize<Dictionary<string, DesktopEntry>>(File.ReadAllText(ObservedPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _observed = new(); }
    }
    public void Refresh()
    {
        var entries = new List<DesktopEntry>();
        entries.AddRange(OpenWindows.List());
        entries.AddRange(_observed.Values.Where(e => File.Exists(e.Target)));
        foreach (var path in _custom.Where(p => File.Exists(p) || Directory.Exists(p))) entries.Add(CustomEntry(path));
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            foreach (var file in Files(root, true).Where(f => Path.GetExtension(f).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".appref-ms", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("卸载")) continue;
                entries.Add(new("app:" + file, name, "开始菜单 · " + Path.GetFileName(Path.GetDirectoryName(file)), "app", file, "▦"));
            }
        }
        foreach (var file in Files(Environment.GetFolderPath(Environment.SpecialFolder.Recent), false).Where(f => Path.GetExtension(f).Equals(".lnk", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc).Take(40))
            entries.Add(new DesktopEntry("recent:" + file, Path.GetFileNameWithoutExtension(file), "Windows 最近访问 · " + File.GetLastWriteTime(file).ToString("MM/dd HH:mm"), "recent", file, "◷") { LastUsed = File.GetLastWriteTimeUtc(file) });
        foreach (var command in Commands.All.Where(c => c.Kind != CommandKind.Assistant))
            entries.Add(new("command:" + command.Id, command.Name, command.Kind == CommandKind.Window ? "操作当前窗口" : "系统快捷操作", "command", command.Id, command.Symbol));
        AddFolder(entries, "桌面文件夹", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddFolder(entries, "文档文件夹", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddFolder(entries, "图片文件夹", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        _entries.Clear(); _entries.AddRange(entries.DistinctBy(e => e.Id));
    }
    private static IEnumerable<string> Files(string root, bool recursive)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return [];
        try { return Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToArray(); }
        catch (IOException) { return []; }
    }
    private static void AddFolder(List<DesktopEntry> entries, string name, string path)
    { if (!string.IsNullOrEmpty(path)) entries.Add(new("folder:" + path, name, path, "folder", path, "▱")); }
    internal static bool IsRecent(DesktopEntry entry, UsageRecord? usage)
    {
        if (entry.Kind != "recent" && (usage?.Count ?? 0) == 0) return false;
        var latest = entry.LastUsed ?? DateTime.MinValue;
        if (usage != null && usage.LastUsed > latest) latest = usage.LastUsed;
        return usage?.RemovedFromRecentAt is not { } removed || latest > removed;
    }
    public IEnumerable<DesktopEntry> Recent() => _entries.Where(e => IsRecent(e, _usage.GetValueOrDefault(e.Id)))
        .OrderByDescending(e => _usage.TryGetValue(e.Id, out var usage) && usage.LastUsed > (e.LastUsed ?? DateTime.MinValue) ? usage.LastUsed : e.LastUsed).Take(40);
    public bool IsHiddenFromFrequent(DesktopEntry entry) => _usage.TryGetValue(entry.Id, out var usage) && usage.HiddenFromFrequent;
    public IEnumerable<DesktopEntry> Frequent() => _entries.Where(e => _usage.ContainsKey(e.Id) && !_usage[e.Id].HiddenFromFrequent && (_usage[e.Id].Count > 0 || _usage[e.Id].Pinned))
        .OrderByDescending(e => _usage[e.Id].Pinned).ThenByDescending(e => HabitScore(_usage[e.Id], DateTime.UtcNow)).Take(24);
    public static double HabitScore(UsageRecord usage, DateTime now) => usage.Count / (1 + Math.Max(0, (now - usage.LastUsed).TotalDays) / 14);
    private static DesktopEntry CustomEntry(string path) => new("custom:" + path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), "自定义入口 · " + path, Directory.Exists(path) ? "folder" : "custom", path, "◇");
    public void AddCustom(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(p => File.Exists(p) || Directory.Exists(p)))
        {
            if (!_custom.Contains(path, StringComparer.OrdinalIgnoreCase)) _custom.Add(path);
            var entry = CustomEntry(path);
            if (!_usage.TryGetValue(entry.Id, out var usage)) _usage[entry.Id] = usage = new();
            usage.Pinned = true; usage.HiddenFromFrequent = false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(CustomPath)!);
        File.WriteAllText(CustomPath + ".tmp", JsonSerializer.Serialize(_custom)); File.Move(CustomPath + ".tmp", CustomPath, true); SaveUsage();
    }
    public bool IsPinned(DesktopEntry entry) => _usage.TryGetValue(entry.Id, out var usage) && usage.Pinned;
    public void RemoveRecent(DesktopEntry entry)
    {
        _usage.TryGetValue(entry.Id, out var previous);
        var cutoff = DateTime.UtcNow;
        if (entry.LastUsed > cutoff) cutoff = entry.LastUsed.Value;
        if (previous?.LastUsed > cutoff) cutoff = previous.LastUsed;
        _usage[entry.Id] = new UsageRecord
        {
            Count = previous?.Count ?? 0, LastUsed = previous?.LastUsed ?? DateTime.MinValue,
            Pinned = previous?.Pinned ?? false, RemovedFromRecentAt = cutoff, HiddenFromFrequent = previous?.HiddenFromFrequent ?? false
        };
        try { SaveUsage(); }
        catch
        {
            if (previous == null) _usage.Remove(entry.Id); else _usage[entry.Id] = previous;
            throw;
        }
    }
    public DesktopEntry WithUsage(DesktopEntry entry) => _usage.TryGetValue(entry.Id, out var usage) ? entry with { OpenCount = usage.Count, Pinned = usage.Pinned, LastUsed = usage.LastUsed } : entry;
    public void RemoveFrequent(DesktopEntry entry)
    {
        _usage.TryGetValue(entry.Id, out var previous);
        _usage[entry.Id] = new UsageRecord
        {
            Count = previous?.Count ?? 0, LastUsed = previous?.LastUsed ?? DateTime.MinValue,
            Pinned = false, HiddenFromFrequent = true, RemovedFromRecentAt = previous?.RemovedFromRecentAt
        };
        try { SaveUsage(); }
        catch
        {
            if (previous == null) _usage.Remove(entry.Id); else _usage[entry.Id] = previous;
            throw;
        }
    }
    public void TogglePin(DesktopEntry entry)
    { if (!_usage.TryGetValue(entry.Id, out var usage)) _usage[entry.Id] = usage = new(); usage.Pinned = !usage.Pinned; if (usage.Pinned) usage.HiddenFromFrequent = false; SaveUsage(); }
    public void Record(DesktopEntry entry)
    { if (!_usage.TryGetValue(entry.Id, out var usage)) _usage[entry.Id] = usage = new(); usage.Count++; usage.LastUsed = DateTime.UtcNow; SaveUsage(); }
    public void Observe(DesktopEntry entry)
    {
        if (!_observed.ContainsKey(entry.Id))
        {
            _observed[entry.Id] = entry; _entries.Add(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(ObservedPath)!);
            File.WriteAllText(ObservedPath + ".tmp", JsonSerializer.Serialize(_observed)); File.Move(ObservedPath + ".tmp", ObservedPath, true);
        }
        // One counted visit per minute prevents rapid Alt+Tab from dominating frequency.
        if (_usage.TryGetValue(entry.Id, out var usage) && (DateTime.UtcNow - usage.LastUsed).TotalMinutes < 1) return;
        Record(entry);
    }
    private void SaveUsage()
    { Directory.CreateDirectory(Path.GetDirectoryName(UsagePath)!); var temporary = UsagePath + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(_usage)); File.Move(temporary, UsagePath, true); }
    public IEnumerable<DesktopEntry> Search(string text)
    {
        var query = text.Trim();
        if (query.Length == 0) return Frequent().Concat(_entries.Where(e => e.Kind == "app").OrderBy(e => e.Name)).DistinctBy(e => e.Id).Take(30);
        var intent = MatchIntent(query);
        var nameQuery = query;
        foreach (var prefix in new[] { "请帮我打开", "帮我打开", "帮我启动", "打开", "启动", "运行", "搜索", "查找" })
            if (nameQuery.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { nameQuery = nameQuery[prefix.Length..].Trim(); break; }
        if (nameQuery.Length == 0) return [];
        return _entries.Select(e => (Entry: e, Score: Score(e, nameQuery, intent))).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => _usage.TryGetValue(x.Entry.Id, out var usage) ? (usage.Pinned ? 1000 : 0) + HabitScore(usage, DateTime.UtcNow) : 0).Take(40).Select(x => x.Entry);
    }
    public static int Score(DesktopEntry entry, string text, string? intent)
    {
        if (entry.Kind == "command" && entry.Target == intent) return 100;
        if (entry.Name.Equals(text, StringComparison.OrdinalIgnoreCase)) return 120;
        if (entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) return 70;
        if (entry.Detail.Contains(text, StringComparison.OrdinalIgnoreCase)) return 20;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && parts.All(p => entry.Name.Contains(p, StringComparison.OrdinalIgnoreCase))) return 60;
        if (text.Length >= 2)
        {
            var name = entry.Name.ToLowerInvariant(); var pattern = text.ToLowerInvariant().Replace(" ", ""); int index = 0;
            foreach (var character in name) if (index < pattern.Length && character == pattern[index]) index++;
            if (index == pattern.Length) return 35;
        }
        return 0;
    }
    public static string? MatchIntent(string query)
    {
        if ((query.Contains("右边") || query.Contains("右侧")) && query.Contains("窗口")) return "snapRight";
        if ((query.Contains("左边") || query.Contains("左侧")) && query.Contains("窗口")) return "snapLeft";
        foreach (var (words, id) in new (string[], string)[]
        {
            (["下载"], "downloads"), (["任务管理器"], "taskManager"),
            (["切换应用", "上一个应用", "上个窗口"], "switchApp"), (["全部窗口", "所有窗口", "任务视图"], "taskView"),
            (["最大化", "放大窗口"], "maximize"), (["最小化", "收起窗口"], "minimize"), (["还原窗口"], "restore"),
            (["置顶"], "topmost"), (["另一块屏幕", "下一屏", "另一个显示器"], "nextMonitor"),
            (["声音大", "音量大", "调高音量", "增大音量"], "volumeUp"), (["声音小", "音量小", "调低音量", "减小音量"], "volumeDown"),
            (["静音", "取消静音"], "mute"), (["暂停音乐", "播放音乐", "暂停播放"], "playPause"),
            (["下一首"], "nextTrack"), (["上一首"], "previousTrack"), (["显示桌面", "回到桌面"], "desktop"),
            (["截图", "截屏"], "screenshot"), (["系统设置"], "settings"), (["文件管理", "资源管理器"], "explorer")
        }) if (words.Any(query.Contains)) return id;
        return null;
    }
    public static void Open(DesktopEntry entry)
    {
        if (entry.Kind == "window") { if (!DesktopActions.Activate(nint.Parse(entry.Target))) throw new InvalidOperationException("这个窗口已关闭或暂时无法切换，请刷新列表。"); }
        else Process.Start(new ProcessStartInfo(entry.Target) { UseShellExecute = true });
    }
}
