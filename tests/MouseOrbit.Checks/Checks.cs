using MouseOrbit;

internal static class Checks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); count++; }
        Check(WheelMath.Sector(0, 0) == -1, "center cancels");
        Check(PointerPress.IsTap(100, false), "short stationary click opens nearby panel");
        Check(!PointerPress.IsTap(300, false), "held click selects wheel");
        Check(!PointerPress.IsTap(100, true), "fast stroke is not treated as tap");
        Check(!PointerPress.IsTap(PointerPress.HoldMilliseconds, false), "hold threshold has deterministic boundary");
        Check(WheelMath.Sector(47, 0) == -1, "dead zone");
        for (int i = 0; i < 8; i++)
        {
            var angle = (i * 45 - 90) * Math.PI / 180;
            Check(WheelMath.Sector(100 * Math.Cos(angle), 100 * Math.Sin(angle)) == i, "sector " + i);
        }
        Check(WheelMath.Sector(-1000, 0) == 6, "negative multi-monitor coordinates");
        var jitter = new GestureRecognizer(0, 0); jitter.Add(8, 4); jitter.Add(-8, 2);
        Check(!jitter.HasMovement && jitter.Pattern == "", "tap and jitter preserve context menu");
        var line = new GestureRecognizer(100, 100); line.Add(60, 100); line.Add(20, 100); line.Add(-20, 100);
        Check(line.Pattern == "L", "repeated direction collapses");
        var turn = new GestureRecognizer(0, 0); turn.Add(0, 50); turn.Add(60, 50);
        Check(turn.Pattern == "DR", "corner gesture");
        var reverse = new GestureRecognizer(0, 0); reverse.Add(0, -60); reverse.Add(0, 20);
        Check(reverse.Pattern == "UD", "reversal gesture");
        var settings = new Preferences();
        Check(settings.Wheel.Length == 8, "eight wheel commands");
        Check(settings.Wheel.Concat(settings.GesturesMap.Values).All(id => Commands.All.Any(c => c.Id == id)), "all bindings resolve");
        Check(Commands.All.Select(c => c.Id).Distinct().Count() == Commands.All.Length, "unique commands");
        Check(Enumerable.Range(0, 3).All(p => settings.GetWheel(p).Length == 8 && settings.GetWheel(p).All(id => Commands.All.Any(c => c.Id == id))), "all three wheels resolve");
        Check(settings.GesturesMap["U"] == "maximize" && settings.GesturesMap["D"] == "minimize", "system gestures by default");
        Check(Launcher.MatchIntent("把当前窗口移到右边") == "snapRight", "window intent");
        Check(Launcher.MatchIntent("打开下载文件夹") == "downloads", "folder intent");
        Check(Launcher.MatchIntent("请调高音量") == "volumeUp", "audio intent");
        Check(Launcher.MatchIntent("打开不存在的应用xyz") == null, "unknown intent does not invent actions");
        Check(Commands.Find("maximize").Kind == CommandKind.Window, "native window action");
        var sample = new DesktopEntry("fixture", "Visual Studio Code", "应用", "app", "unused", "□");
        Check(Launcher.Score(sample, "vsc", null) > 0, "abbreviation search");
        Check(Launcher.Score(sample, "visual code", null) > 0, "multi-word search");
        Check(Launcher.Score(sample, "not-an-app", null) == 0, "unmatched search");
        var now = DateTime.UtcNow;
        Check(Launcher.HabitScore(new UsageRecord { Count = 5, LastUsed = now }, now) > Launcher.HabitScore(new UsageRecord { Count = 5, LastUsed = now.AddDays(-60) }, now), "recent habits outrank stale habits");
        Console.WriteLine($"PASS: {count} checks");
    }
}
