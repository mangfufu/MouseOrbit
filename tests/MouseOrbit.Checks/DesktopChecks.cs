using MouseOrbit;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class DesktopChecks
{
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    public static void Run()
    {
        int count = 0;
        void Check(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); count++; }
        var fixture = new Window { Title = "MouseOrbit verification fixture", Width = 500, Height = 350, ShowActivated = false, Left = 100, Top = 100 };
        fixture.Show(); Pump();
        var handle = new WindowInteropHelper(fixture).Handle;
        try
        {
            Check(DesktopActions.Execute(Commands.Find("maximize"), handle), "maximize API accepted"); Pump();
            Check(fixture.WindowState == WindowState.Maximized, "window maximized");
            Check(DesktopActions.Execute(Commands.Find("restore"), handle), "restore API accepted"); Pump();
            Check(fixture.WindowState == WindowState.Normal, "window restored");
            Check(DesktopActions.Execute(Commands.Find("minimize"), handle), "minimize API accepted"); Pump();
            Check(fixture.WindowState == WindowState.Minimized, "window minimized");
            DesktopActions.Execute(Commands.Find("restore"), handle); Pump();
            Check(DesktopActions.Execute(Commands.Find("snapLeft"), handle), "left half screen"); Pump();
            Check(DesktopActions.Execute(Commands.Find("snapRight"), handle), "right half screen"); Pump();
            Check(DesktopActions.Execute(Commands.Find("topmost"), handle), "pin window");
            Check((Native.GetWindowLong(handle, -20) & 8) != 0, "topmost flag set");
            DesktopActions.Execute(Commands.Find("topmost"), handle);
            Check((Native.GetWindowLong(handle, -20) & 8) == 0, "topmost flag cleared");
            Check(DesktopActions.Execute(Commands.Find("centerWindow"), handle), "center window"); Pump();
            var foreground = Native.GetForegroundWindow();
            var orbit = new OrbitWindow();
            try
            {
                orbit.Open(new Native.Point { X = 600, Y = 400 }, new Preferences()); Pump();
                Check(orbit.IsVisible, "overlay visible");
                Check(Native.GetForegroundWindow() == foreground, "overlay preserves foreground");
                var center = orbit.CenterPixels;
                orbit.Update(new Native.Point { X = (int)center.X, Y = (int)(center.Y - 100 * orbit.Scale) });
                Check(orbit.Visual.Selected == 0, "overlay north sector");
                orbit.Update(new Native.Point { X = (int)center.X, Y = (int)center.Y });
                Check(orbit.Visual.Selected == -1, "overlay center cancellation");
            }
            finally { orbit.Close(); }
            var quick = new QuickPanelWindow();
            var item = new DesktopEntry("fixture", "测试应用", "本地测试", "app", "unused", "□");
            DesktopEntry? selected = null;
            quick.Selected += entry => selected = entry;
            try
            {
                var before = Native.GetForegroundWindow();
                quick.Open(new Native.Point { X = 600, Y = 400 }, new()
                {
                    ["frequent"] = new[] { item, new DesktopEntry("fixture2", "下载文件夹", "系统操作", "command", "downloads", "↓"), new DesktopEntry("fixture3", "浏览器", "应用", "app", "unused", "□") },
                    ["recent"] = Array.Empty<DesktopEntry>(), ["windows"] = Array.Empty<DesktopEntry>()
                });
                Pump();
                Check(quick.IsVisible, "nearby panel opens");
                Check(Native.GetForegroundWindow() == before, "nearby panel preserves foreground");
                Check(quick.Contains(new Native.Point { X = 600, Y = 400 }), "nearby panel is placed around pointer");
                IEnumerable<DependencyObject> Descendants(DependencyObject node)
                {
                    for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i); yield return child;
                        foreach (var nested in Descendants(child)) yield return nested;
                    }
                }
                Check(!Descendants(quick).OfType<System.Windows.Controls.TextBox>().Any(), "nearby panel has no keyboard input field");
                Descendants(quick).OfType<System.Windows.Controls.Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "常用")
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                var screenshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)quick.Width, (int)quick.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                screenshot.Render(quick);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(screenshot));
                using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "mouseorbit-nearby-qa.png"))) png.Save(stream);
                var button = Descendants(quick).OfType<System.Windows.Controls.Button>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == item.Name);
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(selected == item && !quick.IsVisible, "single mouse click selects target and closes panel");
            }
            finally { quick.Close(); }
            var explorer = new DesktopEntry("icon-fixture", "Explorer", "", "app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "");
            var realIcon = ShellIcons.Load(explorer).GetAwaiter().GetResult();
            Check(realIcon is System.Windows.Media.Imaging.BitmapSource && realIcon.IsFrozen, "native executable icon loads and crosses threads safely");
            var folder = explorer with { Kind = "folder", Target = Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            Check(ShellIcons.Load(folder).GetAwaiter().GetResult() != null, "native folder icon loads");
            Check(ShellIcons.Load(explorer with { Target = "Z:\\MouseOrbit-missing-fixture.exe" }).GetAwaiter().GetResult() == null, "missing application icon falls back without crashing");
        }
        finally { fixture.Close(); }
        Console.WriteLine($"PASS: {count} Windows integration checks (only test windows manipulated)");
    }
}
