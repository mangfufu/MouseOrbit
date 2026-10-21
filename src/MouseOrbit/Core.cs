namespace MouseOrbit;

public static class PointerPress
{
    public const int HoldMilliseconds = 220;
    public const int TapDistance = 10;
    public static bool IsTap(long milliseconds, bool moved) => milliseconds < HoldMilliseconds && !moved;
}

public static class WheelMath
{
    // Clockwise from north; angle half-sectors meet at the diagonal boundaries.
    public static int Sector(double x, double y, double deadZone = 48)
    {
        if (x * x + y * y < deadZone * deadZone) return -1;
        var degrees = (Math.Atan2(y, x) * 180 / Math.PI + 90 + 360 + 22.5) % 360;
        return (int)(degrees / 45);
    }
}

public sealed class GestureRecognizer
{
    private double _x, _y;
    private readonly double _threshold;
    private readonly List<string> _segments = new();
    public string Pattern => string.Join("", _segments);
    public bool HasMovement { get; private set; }
    public GestureRecognizer(double x, double y, double threshold = 30)
    { _x = x; _y = y; _threshold = threshold; }
    public void Add(double x, double y)
    {
        var dx = x - _x; var dy = y - _y;
        if (dx * dx + dy * dy < _threshold * _threshold) return;
        HasMovement = true;
        var direction = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? "R" : "L") : (dy > 0 ? "D" : "U");
        if (_segments.Count == 0 || _segments[^1] != direction)
        {
            if (_segments.Count < 8) _segments.Add(direction);
            else if (_segments[^1] != "?") _segments.Add("?");
        }
        _x = x; _y = y;
    }
    public static string Arrows(string value) => value.Replace("U", "↑").Replace("D", "↓").Replace("L", "←").Replace("R", "→");
}

public enum CommandKind { Keys, Window, Launch, Assistant, Capture }
public sealed record Command(string Id, string Name, string Symbol, string Shortcut, ushort[] Keys, CommandKind Kind = CommandKind.Keys, string? Argument = null);

public static class Commands
{
    public static readonly Command[] All =
    [
        new("assistant", "窗口切换", "◎", "窗口 · 常用 · 最近", [], CommandKind.Assistant),
        new("taskView", "全部窗口", "▦", "Win + Tab", [0x5B, 0x09]),
        new("switchApp", "切换应用", "⇄", "Alt + Tab", [0x12, 0x09]),
        new("explorer", "文件管理", "▱", "打开资源管理器", [], CommandKind.Launch, "explorer.exe"),
        new("downloads", "下载文件夹", "↓", "打开下载", [], CommandKind.Launch, "shell:Downloads"),
        new("settings", "系统设置", "⚙", "Windows 设置", [], CommandKind.Launch, "ms-settings:"),
        new("taskManager", "任务管理器", "▥", "Ctrl + Shift + Esc", [0x11, 0x10, 0x1B]),
        new("maximize", "最大化/还原", "□", "当前窗口", [], CommandKind.Window),
        new("minimize", "最小化", "−", "当前窗口", [], CommandKind.Window),
        new("restore", "还原窗口", "▣", "当前窗口", [], CommandKind.Window),
        new("snapLeft", "左半屏", "◧", "再次触发还原", [], CommandKind.Window),
        new("snapRight", "右半屏", "◨", "再次触发还原", [], CommandKind.Window),
        new("centerWindow", "居中窗口", "⊡", "居中并适应屏幕", [], CommandKind.Window),
        new("topmost", "窗口置顶", "♧", "切换置顶状态", [], CommandKind.Window),
        new("nextMonitor", "移到下一屏", "⇥", "跨显示器移动", [], CommandKind.Window),
        new("closeWindow", "关闭窗口", "×", "正常关闭当前窗口", [], CommandKind.Window),
        new("volumeUp", "增大音量", "+", "系统音量", [0xAF]),
        new("volumeDown", "减小音量", "−", "系统音量", [0xAE]),
        new("mute", "静音/恢复", "♪", "系统音量", [0xAD]),
        new("playPause", "播放/暂停", "Ⅱ", "当前媒体", [0xB3]),
        new("nextTrack", "下一首", "▷", "当前媒体", [0xB0]),
        new("previousTrack", "上一首", "◁", "当前媒体", [0xB1]),
        new("soundSettings", "声音设置", "♫", "音频设备", [], CommandKind.Launch, "ms-settings:sound"),
        new("displaySettings", "显示设置", "▤", "屏幕与缩放", [], CommandKind.Launch, "ms-settings:display"),
        new("desktopLeft", "上一桌面", "⇠", "Win + Ctrl + ←", [0x5B, 0x11, 0x25]),
        new("desktopRight", "下一桌面", "⇢", "Win + Ctrl + →", [0x5B, 0x11, 0x27]),
        new("newDesktop", "新建桌面", "+", "Win + Ctrl + D", [0x5B, 0x11, 0x44]),
        new("copy", "复制", "⧉", "Ctrl + C", [0x11, 0x43]),
        new("paste", "粘贴", "▣", "Ctrl + V", [0x11, 0x56]),
        new("cut", "剪切", "✂", "Ctrl + X", [0x11, 0x58]),
        new("undo", "撤销", "↶", "Ctrl + Z", [0x11, 0x5A]),
        new("selectAll", "全选", "▧", "Ctrl + A", [0x11, 0x41]),
        new("save", "保存", "◇", "Ctrl + S", [0x11, 0x53]),
        new("search", "查找", "⌕", "Ctrl + F", [0x11, 0x46]),
        new("desktop", "显示桌面", "▤", "Win + D", [0x5B, 0x44]),
        new("back", "后退", "←", "Alt + ←", [0x12, 0x25]),
        new("forward", "前进", "→", "Alt + →", [0x12, 0x27]),
        new("top", "回到顶部", "↑", "Ctrl + Home", [0x11, 0x24]),
        new("bottom", "前往底部", "↓", "Ctrl + End", [0x11, 0x23]),
        new("newTab", "新建标签", "+", "Ctrl + T", [0x11, 0x54]),
        new("closeTab", "关闭标签", "×", "Ctrl + W", [0x11, 0x57]),
        new("reopen", "恢复标签", "↺", "Ctrl + Shift + T", [0x11, 0x10, 0x54]),
        new("refresh", "刷新", "⟳", "F5", [0x74]),
        new("screenshot", "截图 / GIF", "⌗", "区域截取", [], CommandKind.Capture)
    ];
    public static Command Find(string id) => All.FirstOrDefault(c => c.Id == id) ?? All[0];
}

public sealed class Preferences
{
    public string Trigger { get; set; } = "XButton1";
    public bool Gestures { get; set; } = true;
    public bool LearnApplications { get; set; } = true;
    public bool DarkTheme { get; set; }
    public string Theme { get; set; } = "";
    public bool AutoFocus { get; set; } = true;
    public bool ManualFocus { get; set; }
    public List<string> FocusApplications { get; set; } = new();
    public int GestureRevision { get; set; }
    public double Sensitivity { get; set; } = 30;
    public string[] Wheel { get; set; } = ["maximize", "nextMonitor", "snapRight", "minimize", "desktop", "screenshot", "snapLeft", "topmost"];
    public string[] WindowWheel { get; set; } = ["maximize", "nextMonitor", "snapRight", "minimize", "centerWindow", "topmost", "snapLeft", "restore"];
    public string[] MediaWheel { get; set; } = ["volumeUp", "nextTrack", "playPause", "soundSettings", "volumeDown", "displaySettings", "mute", "previousTrack"];
    public static string[] PageNames => ["快捷轮盘"];
    public string[] GetWheel(int page) => page switch { 1 => WindowWheel, 2 => MediaWheel, _ => Wheel };
    public Dictionary<string, string> GesturesMap { get; set; } = new()
    { ["L"] = "snapLeft", ["R"] = "snapRight", ["U"] = "maximize", ["D"] = "minimize", ["O"] = "desktop", ["C"] = "copy", ["V"] = "paste" };
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseOrbit", "settings.json");
    public static string? LoadWarning { get; private set; }
    public static Preferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new() { GestureRevision = 2 };
            var settings = System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new();
            return Normalize(settings);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { LoadWarning = "配置无法读取，已使用默认值：" + ex.Message; return new() { GestureRevision = 2 }; }
    }
    internal static Preferences Normalize(Preferences settings)
    {
        var oldGestures = new Dictionary<string, string> { ["L"] = "back", ["R"] = "forward", ["U"] = "top", ["D"] = "bottom", ["UD"] = "refresh", ["DR"] = "closeTab", ["DU"] = "newTab", ["LU"] = "reopen" };
        if (settings.GesturesMap?.Count == oldGestures.Count && oldGestures.All(p => settings.GesturesMap.TryGetValue(p.Key, out var value) && value == p.Value))
            settings.GesturesMap = new Preferences().GesturesMap;
        if (settings.GestureRevision < 2)
        {
            var updated = new Preferences().GesturesMap;
            foreach (var key in new[] { "L", "R", "U", "D" })
                if (settings.GesturesMap?.TryGetValue(key, out var custom) == true && Commands.All.Any(c => c.Id == custom)) updated[key] = custom;
            settings.GesturesMap = updated; settings.GestureRevision = 2;
        }
        settings.FocusApplications ??= new();
        if (settings.Wheel?.SequenceEqual(new[] { "assistant", "taskView", "explorer", "downloads", "desktop", "screenshot", "settings", "switchApp" }) == true) settings.Wheel = new Preferences().Wheel;
        if (settings.Wheel?.SequenceEqual(new[] { "copy", "paste", "cut", "undo", "desktop", "screenshot", "search", "save" }) == true) settings.Wheel = new Preferences().Wheel;
        if (Enumerable.Range(0, 3).Any(p => settings.GetWheel(p) is not { Length: 8 } || settings.GetWheel(p).Any(id => !Commands.All.Any(c => c.Id == id))) || settings.GesturesMap is null || settings.GesturesMap.Values.Any(id => !Commands.All.Any(c => c.Id == id)))
            throw new InvalidDataException("配置内容不完整");
        settings.Sensitivity = double.IsFinite(settings.Sensitivity) ? Math.Clamp(settings.Sensitivity, 16, 70) : 30;
        if (settings.Trigger is not ("XButton1" or "XButton2" or "Middle")) settings.Trigger = "XButton1";
        var appearance = ThemePresets.Find(settings.Theme, settings.DarkTheme);
        settings.Theme = appearance.Id; settings.DarkTheme = appearance.Dark;
        return settings;
    }
    public void Save()
    {
        GestureRevision = 2;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
