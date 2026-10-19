internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--render")) { OffscreenPreview.Run(); return; }
        Checks.Run();
        RecentChecks.Run();
        RedesignChecks.Run();
        if (args.Contains("--desktop")) { _ = new System.Windows.Application(); DesktopChecks.Run(); }
    }
}
