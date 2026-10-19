using System.Configuration;
using System.Data;
using System.Windows;

namespace MouseOrbit;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private System.Threading.Mutex? _mutex;
    internal OrbitController? Controller;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new System.Threading.Mutex(true, "MouseOrbit.Desktop.Singleton", out bool created);
        if (!created) { if (!e.Args.Contains("--background")) MessageBox.Show("MouseOrbit 已在运行，请从系统托盘打开设置。", "MouseOrbit"); Shutdown(); return; }
        try
        {
            Controller = new OrbitController();
            var manager = new MainWindow(Controller);
            MainWindow = manager;
            await manager.InitializeLauncher();
            if (!e.Args.Contains("--background")) Controller.OpenAssistant(false);
        }
        catch (Exception ex)
        {
            if (!e.Args.Contains("--background")) MessageBox.Show(ex.Message, "MouseOrbit 启动失败");
            else
            {
                try
                {
                    string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseOrbit");
                    System.IO.Directory.CreateDirectory(folder);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "startup-error.log"), DateTime.Now + "\n" + ex);
                }
                catch (Exception) { }
            }
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    { Controller?.Dispose(); _mutex?.Dispose(); base.OnExit(e); }
}
