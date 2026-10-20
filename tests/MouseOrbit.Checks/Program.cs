internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--perf"))
        {
            if (args.Contains("--software")) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            UiPerformanceChecks.Run(); return;
        }
        if (args.Contains("--ui-checks"))
        {
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            UiResponsivenessChecks.Run(); return;
        }
        if (args.Contains("--render")) { OffscreenPreview.Run(); return; }
        if (args.Contains("--startup-checks"))
        {
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            PreloadChecks.RunStartup(); return;
        }
        Checks.Run();
        RecentChecks.Run();
        RedesignChecks.Run();
        CaptureFeatureChecks.Run();
        InputPipelineChecks.Run();
        PreloadChecks.Run();
        if (args.Contains("--desktop"))
        {
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            DesktopChecks.Run(); InputPipelineChecks.RunHookThread(); InputPipelineChecks.RunController(); InputPipelineChecks.RunGraphicsThread();
            UiResponsivenessChecks.Run();
        }
    }
}
