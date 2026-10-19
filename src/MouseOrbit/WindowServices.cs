using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseOrbit;

internal static class WindowNative
{
    internal delegate bool EnumProc(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Box { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo
    { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Box CaretRect; }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Box rect);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Native.Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    internal static bool IsDesktopClass(string name) => name is "Progman" or "WorkerW" or "#32769" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    internal static bool IsDesktopSurface(nint window)
    {
        if (window == 0) return false;
        if (window == GetDesktopWindow() || window == GetShellWindow()) return true;
        var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
        return IsDesktopClass(name.ToString());
    }
    internal static bool IsOwn(nint window)
    { GetWindowThreadProcessId(window, out uint pid); return pid == Environment.ProcessId; }
    internal static nint RootAt(Native.Point point)
    {
        var root = GetAncestor(WindowFromPoint(point), 2);
        return IsWindow(root) && !IsOwn(root) && OpenWindows.Title(root) is not ("" or "Program Manager") ? root : 0;
    }
    internal static bool Cloaked(nint window) => DwmGetWindowAttribute(window, 14, out int value, 4) == 0 && value != 0;
    internal static string ProcessPath(nint window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        try { using var process = Process.GetProcessById((int)pid); return process.MainModule?.FileName ?? process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return ""; }
    }
    internal static bool GameFocused(nint window, Preferences settings)
    {
        if (!settings.AutoFocus || window == 0 || IsOwn(window) || IsDesktopSurface(window)) return false;
        string path = ProcessPath(window);
        if (settings.FocusApplications.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return true;
        if (SHQueryUserNotificationState(out int state) == 0 && state == 3) return true;
        if (!GetWindowRect(window, out var bounds)) return false;
        var screen = System.Windows.Forms.Screen.FromHandle(window).Bounds;
        bool full = bounds.Left <= screen.Left + 2 && bounds.Top <= screen.Top + 2 && bounds.Right >= screen.Right - 2 && bounds.Bottom >= screen.Bottom - 2;
        if (!full) return false;
        // Full-screen optimizations often report BUSY, rather than exclusive D3D.
        bool gamePath = path.Contains("\\steamapps\\common\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\Epic Games\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\Riot Games\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\Genshin Impact", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\Star Rail", StringComparison.OrdinalIgnoreCase);
        return gamePath || state is 2 or 7;
    }
}

internal readonly record struct WindowTarget(nint Handle, uint Process, uint Thread, nint Focus)
{
    internal static WindowTarget Capture(nint handle)
    {
        uint thread = WindowNative.GetWindowThreadProcessId(handle, out uint process);
        var gui = new WindowNative.GuiInfo { Size = (uint)Marshal.SizeOf<WindowNative.GuiInfo>() };
        WindowNative.GetGUIThreadInfo(thread, ref gui);
        return new(handle, process, thread, gui.Focus);
    }
    internal bool Valid => Handle != 0 && WindowNative.IsWindow(Handle) && WindowNative.GetWindowThreadProcessId(Handle, out uint pid) == Thread && pid == Process;
    internal bool ClipboardReady()
    {
        if (!Valid || Native.GetForegroundWindow() != Handle || Focus == 0 || !WindowNative.IsWindow(Focus)) return false;
        var gui = new WindowNative.GuiInfo { Size = (uint)Marshal.SizeOf<WindowNative.GuiInfo>() };
        return WindowNative.GetGUIThreadInfo(Thread, ref gui) && gui.Focus == Focus && WindowNative.GetAncestor(Focus, 2) == Handle;
    }
}

internal sealed record SwitchWindow(nint Handle, uint Process, string AppKey, string AppName, string Title, bool Minimized, int Screen, long Used)
{
    public DesktopEntry Entry => new("window:" + Handle, Title, "", "window", Handle.ToString(), "");
    public DesktopEntry? IconEntry { get; init; }
    public BitmapSource? Preview { get; init; }
    public DesktopEntry Icon => IconEntry ?? Entry;
}
internal sealed record SwitchGroup(string Key, string Name, IReadOnlyList<SwitchWindow> Windows)
{
    public SwitchWindow Recent => Windows[0];
}

internal sealed class WindowCatalog
{
    private readonly Dictionary<(nint, uint), long> _recent = new();
    private nint _foreground;
    public void Observe(nint window)
    {
        if (window == 0 || window == _foreground || WindowNative.IsOwn(window)) return;
        WindowNative.GetWindowThreadProcessId(window, out uint pid);
        _recent[(window, pid)] = DateTime.UtcNow.Ticks; _foreground = window;
        if (_recent.Count > 300) foreach (var key in _recent.Keys.Where(k => !WindowNative.IsWindow(k.Item1)).ToArray()) _recent.Remove(key);
    }
    public IReadOnlyList<SwitchGroup> Read(nint exclude)
    {
        var entries = new List<SwitchWindow>(); var processes = new Dictionary<uint, (string Path, string Name)>(); long seed = DateTime.UtcNow.Ticks - TimeSpan.TicksPerDay;
        WindowNative.EnumWindows((window, _) =>
        {
            if (window == exclude || !WindowNative.IsWindowVisible(window) || WindowNative.Cloaked(window) || (Native.GetWindowLong(window, -20) & 0x80) != 0) return true;
            string title = OpenWindows.Title(window); if (title is "" or "Program Manager") return true;
            WindowNative.GetWindowThreadProcessId(window, out uint pid); if (pid == Environment.ProcessId) return true;
            if (!processes.TryGetValue(pid, out var app))
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    if (process.ProcessName == "MouseOrbit") return true;
                    string path = process.MainModule?.FileName ?? process.ProcessName;
                    string name = process.MainModule?.FileVersionInfo.FileDescription ?? process.ProcessName;
                    app = (path, name); processes[pid] = app;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { app = ("pid:" + pid, title); processes[pid] = app; }
            }
            var screens = System.Windows.Forms.Screen.AllScreens; var screen = System.Windows.Forms.Screen.FromHandle(window);
            entries.Add(new(window, pid, app.Path, app.Name, title, WindowNative.IsIconic(window), Array.FindIndex(screens, s => s.DeviceName == screen.DeviceName) + 1,
                _recent.GetValueOrDefault((window, pid), seed--)));
            return true;
        }, 0);
        return Group(entries, exclude);
    }
    internal static IReadOnlyList<SwitchGroup> Group(IEnumerable<SwitchWindow> windows, nint exclude) => windows.Where(w => w.Handle != exclude)
        .GroupBy(w => w.AppKey, StringComparer.OrdinalIgnoreCase)
        .Select(g => new SwitchGroup(g.Key, g.First().AppName, g.OrderByDescending(w => w.Used).ToArray()))
        .OrderByDescending(g => g.Recent.Used).ToArray();
}

internal static class PanelPlacement
{
    internal static Rect Place(double x, double y, double width, double height, Rect area)
    {
        width = Math.Min(width, area.Width); height = Math.Min(height, area.Height);
        return new(Math.Clamp(x - 82, area.Left, area.Right - width), Math.Clamp(y - 90, area.Top, area.Bottom - height), width, height);
    }
}

internal static class WindowPreviews
{
    private static readonly Dictionary<(nint, uint), BitmapSource> Cache = new();
    private static readonly SemaphoreSlim Slots = new(2);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    internal static BitmapSource? Cached(SwitchWindow window)
    { lock (Cache) return Cache.GetValueOrDefault((window.Handle, window.Process)); }
    internal static async Task<BitmapSource?> Load(SwitchWindow window)
    {
        var key = (window.Handle, window.Process);
        if (window.Minimized) { lock (Cache) return Cache.GetValueOrDefault(key); }
        var job = Task.Run(async () =>
        {
            if (!await Slots.WaitAsync(50)) return null;
            try
            {
                if (!WindowNative.GetWindowRect(window.Handle, out var rect) || rect.Width < 10 || rect.Height < 10 || rect.Width > 8000 || rect.Height > 5000) return null;
                using var bitmap = new System.Drawing.Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    var dc = graphics.GetHdc(); bool ok;
                    try { ok = PrintWindow(window.Handle, dc, 2); } finally { graphics.ReleaseHdc(dc); }
                    if (!ok) return null;
                }
                using var small = new System.Drawing.Bitmap(320, Math.Max(1, (int)(320d * rect.Height / rect.Width)));
                using (var g = System.Drawing.Graphics.FromImage(small)) { g.Clear(System.Drawing.Color.FromArgb(45, 45, 40)); g.DrawImage(bitmap, 0, 0, small.Width, small.Height); }
                var handle = small.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze();
                    lock (Cache) { if (Cache.Count > 100) Cache.Clear(); Cache[key] = source; }
                    return source;
                }
                finally { DeleteObject(handle); }
            }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or InvalidOperationException) { return null; }
            finally { Slots.Release(); }
        });
        var completed = await Task.WhenAny(job, Task.Delay(650));
        if (completed == job) return await job;
        lock (Cache) return Cache.GetValueOrDefault(key);
    }
}
