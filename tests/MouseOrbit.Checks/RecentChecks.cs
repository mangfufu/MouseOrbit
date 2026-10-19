using MouseOrbit;
using System.IO;

internal static class RecentChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception("FAIL: " + name); count++; }
        var root = Path.Combine(Path.GetTempPath(), "MouseOrbit-recent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var document = Path.Combine(root, "fixture.txt"); File.WriteAllText(document, "keep this document");
        try
        {
            var launcher = new Launcher(root); launcher.AddCustom([document]); launcher.Refresh();
            var entry = launcher.Entries.Single(e => e.Target == document);
            launcher.Record(entry);
            Check(launcher.Recent().Any(e => e.Id == entry.Id), "used file appears in recent");
            launcher.RemoveRecent(entry);
            Check(!launcher.Recent().Any(e => e.Id == entry.Id), "removed file disappears immediately");
            Check(launcher.IsPinned(entry) && launcher.Frequent().Any(e => e.Id == entry.Id), "removing recent preserves pins and frequency");
            Check(File.ReadAllText(document) == "keep this document", "original file remains intact");
            var restarted = new Launcher(root); restarted.Refresh();
            Check(!restarted.Recent().Any(e => e.Id == entry.Id), "removal survives restart and refresh");
            System.Threading.Thread.Sleep(2); restarted.Record(entry);
            Check(restarted.Recent().Any(e => e.Id == entry.Id), "new explicit use restores entry");
            Directory.CreateDirectory(Path.Combine(root, "usage.json.tmp"));
            bool failed = false;
            try { restarted.RemoveRecent(entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed && restarted.Recent().Any(e => e.Id == entry.Id), "failed persistence keeps entry visible");
            Directory.Delete(Path.Combine(root, "usage.json.tmp"));

            restarted.RemoveFrequent(entry);
            Check(!restarted.Frequent().Any(e => e.Id == entry.Id) && !restarted.IsPinned(entry), "frequent removal also unpins entry");
            Check(restarted.Recent().Any(e => e.Id == entry.Id), "frequent removal preserves recent entry");
            restarted.Record(entry);
            Check(!restarted.Frequent().Any(e => e.Id == entry.Id), "automatic usage does not restore removed frequent entry");
            restarted.RemoveRecent(entry);
            Check(restarted.IsHiddenFromFrequent(entry), "recent removal preserves frequent exclusion");
            var afterRemoval = new Launcher(root); afterRemoval.Refresh();
            Check(!afterRemoval.Frequent().Any(e => e.Id == entry.Id) && afterRemoval.IsHiddenFromFrequent(entry), "frequent exclusion survives restart");
            afterRemoval.TogglePin(entry);
            Check(afterRemoval.Frequent().Any(e => e.Id == entry.Id), "explicit pin restores removed frequent entry");
            afterRemoval.RemoveFrequent(entry); afterRemoval.AddCustom([document]);
            Check(afterRemoval.Frequent().Any(e => e.Id == entry.Id), "explicit drop restores custom frequent entry");
            var defaultItem = afterRemoval.Entries.Single(e => e.Id == "command:downloads");
            afterRemoval.RemoveFrequent(defaultItem);
            Check(afterRemoval.IsHiddenFromFrequent(defaultItem), "unused default shortcut can be excluded");
            Check(new Launcher(root).IsHiddenFromFrequent(defaultItem), "default exclusion is persisted");
            Directory.CreateDirectory(Path.Combine(root, "usage.json.tmp")); failed = false;
            try { afterRemoval.RemoveFrequent(entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed && afterRemoval.IsPinned(entry) && afterRemoval.Frequent().Any(e => e.Id == entry.Id), "failed frequent removal restores pin and visibility");
            Directory.Delete(Path.Combine(root, "usage.json.tmp"));
            Check(File.ReadAllText(document) == "keep this document", "frequent removal never deletes the original file");

            var cutoff = DateTime.UtcNow;
            var imported = new DesktopEntry("recent:fixture", "fixture", "", "recent", "fixture.lnk", "") { LastUsed = cutoff.AddDays(-1) };
            var removal = new UsageRecord { RemovedFromRecentAt = cutoff };
            Check(!Launcher.IsRecent(imported, removal), "unchanged Windows recent shortcut remains hidden");
            Check(Launcher.IsRecent(imported with { LastUsed = cutoff.AddSeconds(1) }, removal), "updated Windows recent shortcut reappears");
            Check(!Launcher.IsRecent(imported with { LastUsed = cutoff }, removal), "equal timestamps do not resurrect old entries");
            var app = imported with { Id = "app:fixture", Kind = "app", LastUsed = null };
            Check(!Launcher.IsRecent(app, new UsageRecord { Count = 10, LastUsed = cutoff.AddSeconds(-1), RemovedFromRecentAt = cutoff }), "recent application can be removed");
            Check(Launcher.IsRecent(app, new UsageRecord { Count = 11, LastUsed = cutoff.AddSeconds(1), RemovedFromRecentAt = cutoff }), "new application visit reappears");
        }
        finally
        {
            // Only remove files created within this unique fixture directory; never application data.
            foreach (var name in new[] { "fixture.txt", "usage.json", "shortcuts.json", "usage.json.tmp" })
            { var path = Path.Combine(root, name); if (File.Exists(path)) File.Delete(path); }
            if (Directory.Exists(Path.Combine(root, "usage.json.tmp"))) Directory.Delete(Path.Combine(root, "usage.json.tmp"));
            Directory.Delete(root);
        }
        Console.WriteLine($"PASS: {count} recent-history checks (isolated storage)");
    }
}
